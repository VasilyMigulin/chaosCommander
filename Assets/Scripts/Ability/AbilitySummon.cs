using System;
using System.Collections.Generic;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;
using UnityEngine;

namespace Game.Core.Ability
{
    // === helper === Фронт-ряд владельца: порядок колонок и поиск свободных клеток. Общий для
    // призыва существующих (SummonEffect) и заполнения токенами/копиями (FillRowEffect).
    internal static class BoardFrontRow
    {
        // ГЕОМЕТРИЯ (owner-relative, как в RunSelectCellBoardSystem): ряд 0 — БЛИЖНИЙ к своему аватару
        // (сюда призываются существа; историческое имя FrontRow сохранено — «фронт призыва»), ряд 1 —
        // ДАЛЬНИЙ от аватара, примыкает к линии соприкосновения сторон (пересечение row1→row1 у ИИ).
        public const int FrontRow = 0;
        public const int FarRow   = 1;
        static readonly int[] ColOrder = { 2, 1, 3, 0, 4 };  // центр → влево → вправо → дальше
        const int BoardCols = 5;

        /// <summary>Свободные колонки фронт-ряда владельца, в порядке заполнения [2,1,3,0,4]. Исключает и
        /// фактически занятые (BoardPositionComponent), и зарезервированные в этом резолве (SummonScratch).</summary>
        public static List<int> FreeCells(EcsWorld world, int ownerId) => FreeCellsInRow(world, ownerId, FrontRow);

        /// <summary>То же для ПРОИЗВОЛЬНОГО ряда стороны (FarRow — «заполните сторону», Королёвский сорняк).</summary>
        public static List<int> FreeCellsInRow(EcsWorld world, int ownerId, int row)
        {
            var posPool = world.GetPool<BoardPositionComponent>();
            var occupied = new bool[BoardCols];
            foreach (var e in world.Filter<CreatureTag>().Inc<BoardTag>().Inc<BoardPositionComponent>().Exc<DeadTag>().End())
            {
                ref var p = ref posPool.Get(e);
                if (p.OwnerId != ownerId || p.Row != row) continue;
                if (p.Col >= 0 && p.Col < BoardCols) occupied[p.Col] = true;
            }
            var free = new List<int>();
            foreach (var c in ColOrder)
                if (!occupied[c] && !SummonScratch.IsCellClaimed(ownerId, row, c)) free.Add(c);
            return free;
        }

        /// <summary>Взять первую свободную клетку фронта и СРАЗУ зарезервировать её на этот резолв
        /// (чтобы следующий спавн в том же резолве — напр. RepeatEffect — взял другую). -1 если фронт полон.</summary>
        public static int ClaimFreeCell(EcsWorld world, int ownerId) => ClaimFreeCellInRow(world, ownerId, FrontRow);

        /// <summary>То же для произвольного ряда (дальний — при заполнении всей стороны).</summary>
        public static int ClaimFreeCellInRow(EcsWorld world, int ownerId, int row)
        {
            var free = FreeCellsInRow(world, ownerId, row);
            if (free.Count == 0) return -1;
            int col = free[0];
            SummonScratch.ClaimCell(ownerId, row, col);
            return col;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ПРИЗЫВ СУЩЕСТВУЮЩИХ СУЩНОСТЕЙ (колода/рука/лимбо → борд).
    //
    // Базовый SummonEffect знает ТОЛЬКО «как поставить» (InvokeSummon): владелец берётся
    // от карты-источника, клетка — свободная на фронт-ряду «от центра наружу» [2,1,3,0,4]
    // (как просил пользователь: центр, слева, справа, ...). «Что призывать» решает наследник
    // в SelectSummonEntities.
    //
    // Призыв НОВЫХ сущностей (токены/копии/пулы) — отдельное семейство GenerateCardEffect
    // (через CreateCardEvent по ICreatable.ExpansionId/CardId, sync-safe). Здесь — только
    // уже существующие сущности, поэтому установка идёт штатным пайплайном борда.
    //
    // СИНК: размещение делает ТОЛЬКО активный клиент (InvokeSummon гейтит TurnGate.IsLocalActive),
    // призванное существо синкается своим ActionCastData как обычный розыгрыш — пассив не ре-селектит
    // и не ре-размещает (он и не знает скрытую колоду/руку оппонента). Тай-брейк по индексу всё равно
    // оставлен (детерминизм/предсказуемость на активе). КАВЕАТ: модификаторы призыва ещё не синкаются.
    // См. [[project-sync-replay-authoritative]].
    // ─────────────────────────────────────────────────────────────────────────
    public abstract class SummonEffect : EffectBase, ISummonModifierProvider
    {
        public override Game.Core.Shared.Interface.AiEffectRole AiRole => Game.Core.Shared.Interface.AiEffectRole.Summon;
        /// <summary>
        /// Модификаторы призыва: применяются к КАЖДОМУ призванному существу сверху обычного призыва
        /// (target = призванная сущность, cardEntity = карта-источник). Пусто → просто призыв.
        /// Любой target-эффект подходит: BuffStatsEffect{+1/+1}, DeathTimerEffect{Turns}, Heal и т.п.
        /// Так «призыв с таймером»/«призыв с баффом» — это композиция, а не отдельные поля/классы.
        /// </summary>
        [SerializeReference] public List<IEffect> SummonModifiers = new();

        // ISummonModifierProvider — пассив достаёт модификаторы отсюда (без ссылки на Game.Core.Ability).
        IReadOnlyList<IEffect> ISummonModifierProvider.SummonModifiers => SummonModifiers;

        /// <summary>Фейрит ли призванное существо своё «при разыгрывании» (battlecry). false = призыв (по
        /// умолч., без каскада); true = «разыграть» (Гомункул — нужна цепочка одноимённых).</summary>
        protected virtual bool SummonedFiresOwnCast => false;

        /// <summary>true → когда ряд призыва (ближний к аватару) полон, призыв ПРОДОЛЖАЕТСЯ на дальний ряд
        /// («заполните сторону», Королёвский сорняк). false (по умолч.) — только ряд призыва, как раньше.</summary>
        protected virtual bool SummonToFarRowWhenFrontFull => false;

        public override void Init(EcsWorld world, int cardEntity, int playerEntity)
        {
            base.Init(world, cardEntity, playerEntity);
            if (SummonModifiers != null)
                foreach (var mod in SummonModifiers)
                    mod?.Init(world, cardEntity, playerEntity);
        }

        public override void Dispose()
        {
            base.Dispose();
            if (SummonModifiers != null)
                foreach (var mod in SummonModifiers)
                    mod?.Dispose();
        }

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (!TurnGate.IsLocalActive(world)) return;   // размещение существующих — только активный клиент

            var ownerPool = world.GetPool<OwnerComponent>();
            if (!ownerPool.Has(cardEntity)) return;
            int ownerId = ownerPool.Get(cardEntity).OwnerId;

            // Клетку резервируем на КАЖДЫЙ призыв (ClaimFreeCell): размещение идёт через MoveCardToBoardEvent
            // (позиция выставится позже в кадре), резерв в SummonScratch не даёт следующему спавну в этом
            // резолве сесть на ту же клетку (мульти-призыв / RepeatEffect).
            var selected = new List<int>(SelectSummonEntities(world, cardEntity));   // ВРЕМЕННО: материализуем для лога
            int placed = 0;
            foreach (var summon in selected)
            {
                if (summon < 0) continue;
                int row = BoardFrontRow.FrontRow;
                int col = BoardFrontRow.ClaimFreeCellInRow(world, ownerId, row);
                if (col < 0 && SummonToFarRowWhenFrontFull)                // ряд призыва полон → на дальний
                {
                    row = BoardFrontRow.FarRow;
                    col = BoardFrontRow.ClaimFreeCellInRow(world, ownerId, row);
                }
                if (col < 0) break;                                        // сторона заполнена
                if (!PlaceSummon(world, cardEntity, summon, ownerId, row, col)) continue;
                ApplyModifiers(world, cardEntity, summon);
                placed++;
            }
            // ВРЕМЕННО: Разгром кладбища (SummonFromGrave) не призывал. selected=0 → в зоне нет подходящих существ
            // (нет в кладбище / cost>MaxCost / не CreatureTag). placed<selected → нет свободных клеток фронта.
            UnityEngine.Debug.Log($"[Summon] {GetType().Name} card={cardEntity} owner={ownerId} selected={selected.Count} placed={placed}");
        }

        /// <summary>Какие СУЩЕСТВУЮЩИЕ сущности-существа призвать (детерминированно).</summary>
        protected abstract IEnumerable<int> SelectSummonEntities(EcsWorld world, int cardEntity);

        void ApplyModifiers(EcsWorld world, int sourceCard, int summonEntity)
        {
            if (SummonModifiers == null) return;
            foreach (var mod in SummonModifiers)
            {
                if (mod == null || !mod.IsReady) continue;
                mod.Apply(world, sourceCard, summonEntity);
            }
        }

        /// <summary>
        /// Ставит готовую сущность на ЗАДАННУЮ клетку фронта владельца (col берётся из заранее
        /// посчитанного списка свободных в Apply). MoveCardToBoardEvent + InvokeEvent → штатный пайплайн:
        /// RunMoveCardToBoardSystem снимет зональные теги/уберёт из руки, RunInvokeCreatureSystem
        /// опубликует CardCastEvent (у призванного сработает его собственное «При разыгрывании»).
        ///
        /// СИНК: размещение делает ТОЛЬКО активный клиент (гейт в Apply). Призванное существо синкается
        /// СВОИМ ActionCastData (RunInvokeCreatureSystem → CardCastEvent → CollectActionSystem) — как
        /// обычный розыгрыш: пассив реплеит его и ставит на ту же клетку. Пассив не знает скрытую колоду/
        /// руку оппонента и не смог бы ВЫБРАТЬ, кого звать. КАВЕАТ: модификаторы призыва (бафф/таймер)
        /// применяются только на активе → к пассиву не доезжают (отдельный TODO синка модификаторов).
        /// </summary>
        protected bool PlaceSummon(EcsWorld world, int sourceCard, int summonEntity, int ownerId, int row, int col)
        {
            // RunMoveCardToBoardSystem снимает DeckTag, но из DeckComponent.CardEntities не убирает — делаем сами.
            RemoveFromDeck(world, summonEntity, ownerId);

            var movePool = world.GetPool<MoveCardToBoardEvent>();
            if (!movePool.Has(summonEntity)) movePool.Add(summonEntity);
            ref var move = ref movePool.Get(summonEntity);
            move.Row = row; move.Col = col; move.OwnerId = ownerId;

            // Призыв ≠ розыгрыш: по умолчанию призванный НЕ фейрит своё «при разыгрывании» (NotCast=true) —
            // иначе Работяга→Работяга уходил бы в каскад. «Разыграть» (Гомункул) переопределяет на true.
            var invokePool = world.GetPool<InvokeEvent>();
            if (!invokePool.Has(summonEntity)) invokePool.Add(summonEntity).NotCast = !SummonedFiresOwnCast;

            // Регистрируем призыв для синка модификаторов: RunResolveAbilityQueueSystem заберёт
            // призванных в AbilityResolvedNetEvent → пассив применит к ним SummonModifiers.
            SummonScratch.Add(summonEntity);
            return true;
        }

        static void RemoveFromDeck(EcsWorld world, int summonEntity, int ownerId)
        {
            var playerPool = world.GetPool<PlayerComponent>();
            var deckPool   = world.GetPool<DeckComponent>();
            foreach (var pe in world.Filter<PlayerComponent>().Inc<DeckComponent>().End())
            {
                if (playerPool.Get(pe).PlayerId != ownerId) continue;
                ref var deck = ref deckPool.Get(pe);
                if (deck.CardEntities != null && deck.CardEntities.Remove(summonEntity))
                    deck.Count = deck.CardEntities.Count;
                return;
            }
        }
    }

    /// <summary>Как выбирать существ в колоде. По стоимости (MostExpensive/LeastExpensive) или по
    /// ИДЕНТИЧНОСТИ: SameAsSelf — такие же, как источник (по своему ID; «другого работягу»); Specific —
    /// конкретная карта по ассету (ICreatable → ID). Детерминированно (порядок колоды); Random — TODO.</summary>
    public enum DeckSummonPick { MostExpensive, LeastExpensive, SameAsSelf, Specific }

    // === class (OOP) === Призыв из колоды владельца. Patriarch: Pick=MostExpensive. Работяга:
    // Pick=SameAsSelf (призвать другого СЕБЯ из колоды). «Умрёт через N» / «+1/+1» — через SummonModifiers.
    [Serializable]
    public sealed class SummonFromDeckEffect : SummonEffect
    {
        public DeckSummonPick Pick = DeckSummonPick.MostExpensive;
        public int Count = 1;

        [Tooltip("Заполнить ВЕСЬ свободный фронт-ряд (Count игнорируется; берём столько, сколько влезет).")]
        public bool FillRow = false;

        [Tooltip("Когда ряд призыва (ближний к аватару) полон — продолжить на ДАЛЬНИЙ ряд, у линии " +
                 "соприкосновения («заполните сторону», Королёвский сорняк). Обычно вместе с FillRow.")]
        public bool IncludeFarRow = false;

        [Tooltip("Только для Pick=Specific: ассет CardInstanceData призываемой карты (перетащить).")]
        public ScriptableObject Card;

        [Tooltip("Призывать только существ стоимостью ≤ MaxCost («всех дешевле X» = LeastExpensive+FillRow+MaxCost). 0 — без ограничения (default: старые ассеты не меняются).")]
        public int MaxCost = 0;

        protected override bool SummonToFarRowWhenFrontFull => IncludeFarRow;

        protected override IEnumerable<int> SelectSummonEntities(EcsWorld world, int cardEntity)
        {
            int limit = FillRow ? int.MaxValue : Count;   // FillRow → Apply обрежет по свободным клеткам
            var ownerPool = world.GetPool<OwnerComponent>();
            if (!ownerPool.Has(cardEntity)) yield break;
            int ownerId = ownerPool.Get(cardEntity).OwnerId;

            foreach (var e in SummonPickUtil.SelectFrom(world, cardEntity, FindDeck(world, ownerId), Pick, limit, Card, MaxCost))
                yield return e;
        }

        static List<int> FindDeck(EcsWorld world, int ownerId)
        {
            var playerPool = world.GetPool<PlayerComponent>();
            var deckPool   = world.GetPool<DeckComponent>();
            foreach (var pe in world.Filter<PlayerComponent>().Inc<DeckComponent>().End())
            {
                if (playerPool.Get(pe).PlayerId != ownerId) continue;
                return deckPool.Get(pe).CardEntities;
            }
            return null;
        }
    }

    // === helper === ВЫБОР существующих существ из списка-зоны (колода/рука) по правилу Pick. Общий для
    // SummonFromDeck/SummonFromHand: по идентичности (SameAsSelf/Specific, исключая источник, в порядке зоны)
    // или по стоимости (MostExpensive/LeastExpensive, тай-брейк по индексу). Детерминировано → выбор на активе.
    // maxCost > 0 — отсечь кандидатов дороже (действует в обеих ветках); 0 — без ограничения
    // (0-сентинел, а не -1: [SerializeReference]-десериализация не гоняет инициализаторы полей,
    // у старых ассетов новое поле = 0 → должно означать «поведение как раньше»).
    internal static class SummonPickUtil
    {
        public static int CostOf(EcsWorld world, int e)
        {
            var g = world.GetPool<GoldCostComponent>();   if (g.Has(e)) return g.Get(e).Cost;
            var m = world.GetPool<ManaCostComponent>();    if (m.Has(e)) return m.Get(e).Cost;
            var h = world.GetPool<HealthCostComponent>();  if (h.Has(e)) return h.Get(e).Cost;
            return 0;
        }

        public static IEnumerable<int> SelectFrom(EcsWorld world, int cardEntity, List<int> zone,
                                                  DeckSummonPick pick, int limit, UnityEngine.ScriptableObject specificCard,
                                                  int maxCost = 0)
        {
            if (zone == null) yield break;
            var creaturePool = world.GetPool<CreatureTag>();

            if (pick == DeckSummonPick.SameAsSelf || pick == DeckSummonPick.Specific)
            {
                if (!TryGetWantedIdentity(world, cardEntity, pick, specificCard, out string wantExp, out int wantId)) yield break;
                var modelPool = world.GetPool<CardModelComponent>();
                int found = 0;
                for (int i = 0; i < zone.Count; i++)
                {
                    int e = zone[i];
                    if (e == cardEntity) continue;                          // «другого» — не сам источник
                    if (!creaturePool.Has(e) || !modelPool.Has(e)) continue;
                    var m = modelPool.Get(e);
                    if (m.ModelId != wantId || m.ExpansionId != wantExp) continue;
                    if (maxCost > 0 && CostOf(world, e) > maxCost) continue;
                    yield return e;
                    if (++found >= limit) yield break;
                }
                yield break;
            }

            var candidates = new List<(int entity, int cost, int index)>();
            for (int i = 0; i < zone.Count; i++)
            {
                int e = zone[i];
                if (!creaturePool.Has(e)) continue;
                int cost = CostOf(world, e);
                if (maxCost > 0 && cost > maxCost) continue;
                candidates.Add((e, cost, i));
            }
            if (candidates.Count == 0) yield break;

            candidates.Sort((a, b) =>
            {
                int byCost = pick == DeckSummonPick.MostExpensive ? b.cost.CompareTo(a.cost) : a.cost.CompareTo(b.cost);
                return byCost != 0 ? byCost : a.index.CompareTo(b.index);
            });

            int take = Math.Min(limit, candidates.Count);
            for (int i = 0; i < take; i++) yield return candidates[i].entity;
        }

        static bool TryGetWantedIdentity(EcsWorld world, int cardEntity, DeckSummonPick pick,
                                         UnityEngine.ScriptableObject specificCard, out string exp, out int modelId)
        {
            if (pick == DeckSummonPick.Specific)
            {
                if (specificCard is ICreatable c) { exp = c.ExpansionId; modelId = c.CardId; return true; }
                exp = null; modelId = -1; return false;
            }
            var pool = world.GetPool<CardModelComponent>();
            if (pool.Has(cardEntity)) { ref var m = ref pool.Get(cardEntity); exp = m.ExpansionId; modelId = m.ModelId; return true; }
            exp = null; modelId = -1; return false;
        }
    }

    // === class (OOP) === Призыв СУЩЕСТВУЮЩИХ существ с КЛАДБИЩА владельца (Разгром кладбища): до Count
    // существ стоимостью ≤ MaxCost. Оживление (снять DeadTag/GraveTag, восстановить HP/скорость) делает
    // RunMoveCardToBoardSystem при размещении — на ОБОИХ клиентах. Синк — как у любого призыва (выбор
    // только на активе, размещение синкается ActionCastData призванного).
    [Serializable]
    public sealed class SummonFromGraveEffect : SummonEffect
    {
        public int MaxCost = 3;
        public int Count = 2;

        protected override IEnumerable<int> SelectSummonEntities(EcsWorld world, int cardEntity)
        {
            var ownerPool = world.GetPool<OwnerComponent>();
            if (!ownerPool.Has(cardEntity)) yield break;
            int ownerId = ownerPool.Get(cardEntity).OwnerId;

            int found = 0;
            foreach (var e in world.Filter<GraveTag>().Inc<CreatureTag>().Inc<OwnerComponent>().End())
            {
                if (ownerPool.Get(e).OwnerId != ownerId) continue;
                if (SummonPickUtil.CostOf(world, e) > MaxCost) continue;
                yield return e;
                if (++found >= Count) yield break;
            }
        }
    }

    // === class (OOP) === Призыв САМОГО СЕБЯ (сущности-ИСТОЧНИКА) на борд — перемещает существующую карту, а
    // НЕ создаёт токен (баффы/идентичность сохраняются). «Выгребной мусор»: на OnDiscard выходит на поле сам +
    // копия (FillRowWithCopyOfSelf{1} СЛЕДУЮЩИМ эффектом — общий SummonScratch/ClaimFreeCell не даст им сесть
    // на одну клетку). Источник может лежать в кладбище (после сброса): RunMoveCardToBoardSystem снимет
    // GraveTag/DeadTag и восстановит существо. Тихий призыв (NotCast=true от SummonEffect) — своё «при
    // разыгрывании» не фейрит. Гейт IsLocalActive + синк ActionCastData — как у всего summon-семейства.
    [Serializable]
    public sealed class SummonSelfEffect : SummonEffect
    {
        protected override IEnumerable<int> SelectSummonEntities(EcsWorld world, int cardEntity)
        {
            // Только существо и только если оно НЕ на борде (сброшено/в зоне) — иначе призывать нечего.
            if (world.GetPool<CreatureTag>().Has(cardEntity) && !world.GetPool<BoardTag>().Has(cardEntity))
                yield return cardEntity;
        }
    }

    // === class (OOP) === ПРИЗЫВ существа из РУКИ владельца на борд (тихий призыв — БЕЗ форса CastEvent, в
    // отличие от PlaySameNameFromHand/PlayCardFromZone, которые РАЗЫГРЫВАЮТ). Выбор как у SummonFromDeck
    // (MostExpensive/LeastExpensive/SameAsSelf/Specific), общий SummonPickUtil. Существо снимается из руки
    // RunMoveCardToBoardSystem при размещении. Синк — как у любого призыва (выбор на активе, размещение
    // синкается ActionCastData призванного).
    [Serializable]
    public sealed class SummonFromHandEffect : SummonEffect
    {
        public DeckSummonPick Pick = DeckSummonPick.MostExpensive;
        public int Count = 1;

        [Tooltip("Заполнить ВЕСЬ свободный фронт-ряд (Count игнорируется).")]
        public bool FillRow = false;

        [Tooltip("Только для Pick=Specific: ассет CardInstanceData призываемой карты (перетащить).")]
        public ScriptableObject Card;

        [Tooltip("Призывать только существ стоимостью ≤ MaxCost. 0 — без ограничения (default: старые ассеты не меняются).")]
        public int MaxCost = 0;

        protected override IEnumerable<int> SelectSummonEntities(EcsWorld world, int cardEntity)
        {
            int limit = FillRow ? int.MaxValue : Count;
            var ownerPool = world.GetPool<OwnerComponent>();
            if (!ownerPool.Has(cardEntity)) yield break;
            int ownerId = ownerPool.Get(cardEntity).OwnerId;

            foreach (var e in SummonPickUtil.SelectFrom(world, cardEntity, FindHand(world, ownerId), Pick, limit, Card, MaxCost))
                yield return e;
        }

        static List<int> FindHand(EcsWorld world, int ownerId)
        {
            var playerPool = world.GetPool<PlayerComponent>();
            var handPool   = world.GetPool<HandComponent>();
            foreach (var pe in world.Filter<PlayerComponent>().Inc<HandComponent>().End())
            {
                if (playerPool.Get(pe).PlayerId != ownerId) continue;
                return handPool.Get(pe).CardEntities;
            }
            return null;
        }
    }

    // PlaySameNameFromHandEffect (Гомункул/Грядущий шторм) переехал в AbilityControl.cs и стал ЕДИНЫМ
    // (EffectBase + PlayCardUtil): сам ветвит существо→призыв / спелл-чара→каст, без SummonEffect-багажа.

    // === class (OOP) === Биба/Боба («при разыгрывании»): призвать конкретную карту-партнёра,
    // ГДЕ БЫ ОН НИ БЫЛ (колода/рука/кладбище/лимбо — кроме уже на поле). Партнёр задаётся ассетом
    // (ICreatable), матчим по ExpansionId+CardId. Призыв существующей сущности → InvokeSummon
    // (синк через ActionCastData призванного). Если партнёра нет в не-боевых зонах — ничего.
    [Serializable]
    public sealed class SummonNamedEffect : SummonEffect
    {
        [Tooltip("Ассет CardInstanceData партнёра (перетащить).")]
        public ScriptableObject Source;

        public int Count = 1;

        protected override IEnumerable<int> SelectSummonEntities(EcsWorld world, int cardEntity)
        {
            var partner = Source as ICreatable;
            if (partner == null) yield break;

            var ownerPool = world.GetPool<OwnerComponent>();
            if (!ownerPool.Has(cardEntity)) yield break;
            int ownerId = ownerPool.Get(cardEntity).OwnerId;

            var modelPool = world.GetPool<CardModelComponent>();
            int found = 0;
            // Не на поле (Exc BoardTag) и живой (Exc DeadTag) — «где бы ни был», кроме уже призванного.
            foreach (var e in world.Filter<CardModelComponent>().Inc<OwnerComponent>().Inc<CreatureTag>().Exc<BoardTag>().Exc<DeadTag>().End())
            {
                if (ownerPool.Get(e).OwnerId != ownerId) continue;
                var m = modelPool.Get(e);   // копия структуры: ref-локали запрещены в итераторах (CS8176)
                if (m.ModelId != partner.CardId || m.ExpansionId != partner.ExpansionId) continue;
                yield return e;
                if (++found >= Count) yield break;
            }
        }
    }
}
