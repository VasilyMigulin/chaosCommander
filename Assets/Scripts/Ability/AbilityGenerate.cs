using System;
using System.Collections.Generic;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;
using UnityEngine;

namespace Game.Core.Ability
{
    // ─────────────────────────────────────────────────────────────────────────
    // ГЕНЕРАЦИЯ НОВЫХ КАРТ (токены/копии) — семейство B. Создаёт НОВУЮ сущность через
    // CreateCardEvent (по идентичности ExpansionId+CardId → CardConfig), в отличие от
    // SummonEffect (двигает СУЩЕСТВУЮЩУЮ). Владелец генерируемой карты = владелец источника.
    //
    // СИНК (без отдельного RPC): пассив РЕ-РАНИТ резолв способности (ReplayActionSystem
    // впрыскивает очередь из ActionAbilityData → RunResolveAbilityQueueSystem применяет
    // эффекты), поэтому Apply выполняется на ОБОИХ клиентах. Единственное требование —
    // одинаковый NetworkEntityKey у порождённой карты: берём ДЕТЕРМИНИРОВАННЫЙ ключ
    // (casterKey + seq из GeneratedCardCounterComponent), а не Guid.NewGuid(). Тогда
    // будущие ссылки на карту резолвятся у обоих. IsEnemy считается от лица КАЖДОГО клиента
    // (по LocalComponent владельца) — у активного это своя карта, у пассива — вражеская.
    //
    // НЕдетерминированная генерация (случайный спелл из пула, discover с выбором) этот путь
    // НЕ покрывает — там активный обязан передать выбранную идентичность+ключ снапшотом
    // (как ActionCardPickedData.CreateFromPool). Сделаем при реализации Окропить/Discover.
    // ─────────────────────────────────────────────────────────────────────────
    public abstract class GenerateCardEffect : EffectBase
    {
        public int Count = 1;   // сколько карт создать (<=0 трактуем как 1 — старые ассеты без поля)
        protected enum Zone { Hand, Deck }

        /// <summary>Куда кладём порождённую карту.</summary>
        protected abstract Zone TargetZone { get; }

        /// <summary>Идентичность создаваемой карты (из ассета-ICreatable или из самого источника).</summary>
        protected abstract bool TryGetCardIdentity(EcsWorld world, int cardEntity, out string expansionId, out int cardId);

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            int n = Count <= 0 ? 1 : Count;   // старый ассет без поля Count сериализуется как 0 → создаём 1
            for (int i = 0; i < n; i++)
            {
                if (!TryGetCardIdentity(world, cardEntity, out string exp, out int cardId)) return;
                Spawn(world, cardEntity, exp, cardId, TargetZone == Zone.Hand);
            }
        }

        // === helpers ===

        /// <summary>
        /// Порождает карту (exp+cardId) у владельца sourceCard: в руку (toHand) или в колоду. Детерм. ключ
        /// + RegisterInZoneList. Переиспользуют под-эффекты семейства И GainRandomCardEffect (internal).
        /// </summary>
        // forceRandomTarget: нужен ли ПОРОЖДЁННОЙ карте авто-выбор цели (Selected → Random), НЕЗАВИСИМО от
        // autoCast. По умолчанию true (старое поведение — все текущие autoCast-вызыватели форсили всегда,
        // как Йогг-Сарон); PlayRandomFromPoolEffect (Фокус-покус) передаёт вычисленное по триггеру значение.
        // modifiers — target-эффекты к порождённой карте при материализации (GeneratedModScratch →
        // CreateCardSystem; ре-ран на обоих клиентах → зеркально), как у SpawnToBoard/дискавера.
        internal static void Spawn(EcsWorld world, int sourceCard, string exp, int cardId, bool toHand, bool autoCast = false, bool forceRandomTarget = true,
                                   IReadOnlyList<IEffect> modifiers = null)
        {
            if (string.IsNullOrEmpty(exp) || cardId < 0) return;

            var ownerPool = world.GetPool<OwnerComponent>();
            if (!ownerPool.Has(sourceCard)) return;
            int ownerId = ownerPool.Get(sourceCard).OwnerId;

            if (!TryFindOwnerPlayer(world, ownerId, out int ownerPlayer, out bool ownerIsLocal)) return;

            string genKey = NextKey(world, sourceCard);
            Debug.Log($"[Generate] создаю exp={exp} cardId={cardId} -> owner={ownerId} (local={ownerIsLocal}) zone={(toHand ? "Hand" : "Deck")} key={genKey} autoCast={autoCast}");
            GameEventBus.Publish(new CreateCardEvent
            {
                ExpansionId        = exp,
                CardId             = cardId,
                NetworkEntityKey   = genKey,
                PlayerOwnerEntity  = ownerPlayer,
                OwnerId            = ownerId,
                IsEnemy            = !ownerIsLocal,
                InHand             = toHand,
                // !InHand && !InBoard && !InGrave → колода (else-ветка CreateCardSystem)
                RegisterInZoneList = !autoCast,   // авто-каст: без UI-зоны (карта тут же разыграется)
                AutoCast           = autoCast,
                ForceRandomTarget  = autoCast && forceRandomTarget,
                // UI летит визуально ОТ карты-источника (кастер способности), а не «из-за края экрана» —
                // как у раскопки (см. HandUISystem/EntityWorldPosUtil). sourceCard может быть уже на
                // кладбище (спелл после резолва) — сущность не удаляется, TryGet сам отфолбэчится на аватар.
                SourceEntity       = sourceCard,
            });
            GeneratedModScratch.Register(genKey, sourceCard, modifiers);
            GameEventBus.Publish(new CardGeneratedEvent { ModelId = cardId, GeneratorPlayerId = Attribution(ownerId) });
        }

        /// <summary>
        /// Порождает карту (exp+cardId) у владельца sourceCard СРАЗУ на клетке борда (row/col). Детерм. ключ
        /// (как Spawn). Для заполнения ряда токенами/копиями (FillRowEffect). NB: InBoard-создание НЕ
        /// публикует CardCastEvent → собственное «при разыгрывании» токена не срабатывает (для токенов ок).
        /// summonModifiers — target-эффекты, применяемые к порождённой сущности при материализации
        /// (через GeneratedModScratch → CreateCardSystem; ре-ран на обоих клиентах → синк даром).
        /// </summary>
        internal static void SpawnToBoard(EcsWorld world, int sourceCard, string exp, int cardId, int row, int col,
                                          IReadOnlyList<IEffect> summonModifiers = null)
        {
            if (string.IsNullOrEmpty(exp) || cardId < 0) return;

            var ownerPool = world.GetPool<OwnerComponent>();
            if (!ownerPool.Has(sourceCard)) return;
            int ownerId = ownerPool.Get(sourceCard).OwnerId;

            if (!TryFindOwnerPlayer(world, ownerId, out int ownerPlayer, out bool ownerIsLocal)) return;

            string genKey = NextKey(world, sourceCard);
            GameEventBus.Publish(new CreateCardEvent
            {
                ExpansionId        = exp,
                CardId             = cardId,
                NetworkEntityKey   = genKey,
                PlayerOwnerEntity  = ownerPlayer,
                OwnerId            = ownerId,
                IsEnemy            = !ownerIsLocal,
                InBoard            = true,
                BoardRow           = row,
                BoardCol           = col,
                BoardOwnerId       = ownerId,
                RegisterInZoneList = false,   // на борде — не в списках руки/колоды
            });
            GeneratedModScratch.Register(genKey, sourceCard, summonModifiers);
            GameEventBus.Publish(new CardGeneratedEvent { ModelId = cardId, GeneratorPlayerId = Attribution(ownerId) });
        }

        /// <summary>
        /// Порождает карту (exp+cardId) в КОЛОДУ ОППОНЕНТА владельца sourceCard (Старый колдун → вонючие
        /// облака). Детерм. ключ (как Spawn). Владелец/IsEnemy считаются от оппонента (на каждом клиенте
        /// корректно: у того, чей это игрок локально, IsEnemy=false). Для замешивания «вреда» в чужую колоду.
        /// </summary>
        internal static void SpawnToOpponentDeck(EcsWorld world, int sourceCard, string exp, int cardId)
        {
            if (string.IsNullOrEmpty(exp) || cardId < 0) return;

            var ownerPool = world.GetPool<OwnerComponent>();
            if (!ownerPool.Has(sourceCard)) return;
            int sourceOwnerId = ownerPool.Get(sourceCard).OwnerId;

            var pp = world.GetPool<PlayerComponent>();
            foreach (var pe in world.Filter<PlayerComponent>().End())
            {
                ref var p = ref pp.Get(pe);
                if (p.PlayerId == sourceOwnerId) continue;   // ищем оппонента
                GameEventBus.Publish(new CreateCardEvent
                {
                    ExpansionId        = exp,
                    CardId             = cardId,
                    NetworkEntityKey   = NextKey(world, sourceCard),
                    PlayerOwnerEntity  = pe,
                    OwnerId            = p.PlayerId,
                    IsEnemy            = !p.IsLocalPlayer,
                    InHand             = false,            // → колода
                    RegisterInZoneList = true,
                });
                GameEventBus.Publish(new CardGeneratedEvent { ModelId = cardId, GeneratorPlayerId = Attribution(sourceOwnerId) });
                return;
            }
        }

        /// <summary>Идентичность из ассета CardInstanceData (через ICreatable). Используют под-эффекты с полем Source.</summary>
        protected static bool IdentityFromAsset(ScriptableObject source, out string expansionId, out int cardId)
        {
            if (source is ICreatable c) { expansionId = c.ExpansionId; cardId = c.CardId; return true; }
            expansionId = null; cardId = -1; return false;
        }

        /// <summary>Кому атрибутировать генерацию для счётчиков «замешано»: инициатору резолва (гранёная
        /// способность — Газовое вздутие → ты), иначе владельцу карты-источника (нативная генерация).
        /// Назначение/владелец СОЗДАВАЕМОЙ карты этим НЕ меняется — только учёт «кем замешано».</summary>
        static int Attribution(int fallbackOwnerId)
            => AbilityResolveContext.OriginOwnerId >= 0 ? AbilityResolveContext.OriginOwnerId : fallbackOwnerId;

        static bool TryFindOwnerPlayer(EcsWorld world, int ownerId, out int playerEntity, out bool isLocal)
        {
            var pp = world.GetPool<PlayerComponent>();
            foreach (var pe in world.Filter<PlayerComponent>().End())
            {
                ref var p = ref pp.Get(pe);
                if (p.PlayerId != ownerId) continue;
                playerEntity = pe; isLocal = p.IsLocalPlayer; return true;
            }
            playerEntity = -1; isLocal = false; return false;
        }

        // Детерминированный ключ: casterKey + порядковый seq (одинаков у обоих клиентов, см. шапку).
        static string NextKey(EcsWorld world, int caster)
        {
            var netPool = world.GetPool<NetworkEntityComponent>();
            string casterKey = netPool.Has(caster) ? netPool.Get(caster).NetworkEntityKey : ("E" + caster);

            var counterPool = world.GetPool<GeneratedCardCounterComponent>();
            if (!counterPool.Has(caster)) counterPool.Add(caster);
            ref var c = ref counterPool.Get(caster);
            int seq = c.Next++;

            return $"{casterKey}~gen{seq}";
        }
    }

    // === class (OOP) === Создать карту из ассета в РУКУ владельца (Водонос → «Освежающий напиток»).
    [Serializable]
    public sealed class CreateCardToHandEffect : GenerateCardEffect
    {
        public override Game.Core.Shared.Interface.AiEffectRole AiRole => Game.Core.Shared.Interface.AiEffectRole.Draw;
        [Tooltip("Ассет CardInstanceData создаваемой карты (перетащить).")]
        public ScriptableObject Source;

        protected override Zone TargetZone => Zone.Hand;

        protected override bool TryGetCardIdentity(EcsWorld world, int cardEntity, out string exp, out int id)
            => IdentityFromAsset(Source, out exp, out id);
    }

    // === class (OOP) === Замешать карту из ассета в КОЛОДУ владельца (Биба → Боба в начале матча).
    [Serializable]
    public sealed class ShuffleCardIntoDeckEffect : GenerateCardEffect
    {
        [Tooltip("Ассет CardInstanceData замешиваемой карты (перетащить).")]
        public ScriptableObject Source;

        protected override Zone TargetZone => Zone.Deck;

        protected override bool TryGetCardIdentity(EcsWorld world, int cardEntity, out string exp, out int id)
            => IdentityFromAsset(Source, out exp, out id);
    }

    // === class (OOP) === Замешать в КОЛОДУ владельца КОПИЮ самого источника (Гомункул, при смерти).
    // Идентичность берётся из CardModelComponent источника — ассет указывать не нужно.
    [Serializable]
    public sealed class ShuffleCopyOfSelfEffect : GenerateCardEffect
    {
        protected override Zone TargetZone => Zone.Deck;

        protected override bool TryGetCardIdentity(EcsWorld world, int cardEntity, out string exp, out int id)
        {
            var pool = world.GetPool<CardModelComponent>();
            if (!pool.Has(cardEntity)) { exp = null; id = -1; return false; }
            ref var m = ref pool.Get(cardEntity);
            exp = m.ExpansionId; id = m.ModelId;
            return true;
        }
    }

    // === class (OOP) === Замешать в КОЛОДУ владельца ИСТОЧНИКА Count копий ЦЕЛЕВОЙ карты («Дополнительные
    // возможности»: кладётся в Modifiers дискавера — target там = выбранная карта; годится и как обычный
    // target-эффект «замешай 3 копии цели»). Идентичность — CardModelComponent цели; владелец копий — владелец
    // источника (Spawn). СИНК ДАРОМ: для discover-из-пула модификаторы применяет CreateCardSystem при
    // материализации выбора на ОБОИХ клиентах (GeneratedModScratch), ключи копий детерминированы (NextKey
    // источника) → обе стороны создают одинаковые копии без спец-канала.
    [Serializable]
    public sealed class ShuffleCopiesOfTargetEffect : EffectBase
    {
        public int Count = 3;

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (target < 0 || Count <= 0) return;
            var pool = world.GetPool<CardModelComponent>();
            if (!pool.Has(target)) return;
            ref var m = ref pool.Get(target);
            // Фидбэк: подсветить, КАКУЮ карту руки скопировал Дупликатор (punch + VFX; no-op если не в руке).
            CardFeedbackUtil.MarkAffectedInHand(world, target, Game.Core.Service.CardAffectKind.Copied);
            for (int i = 0; i < Count; i++)
                GenerateCardEffect.Spawn(world, cardEntity, m.ExpansionId, m.ModelId, toHand: false);
        }
    }

    // === class (OOP) === Призвать на СВОЮ сторону КОПИЮ целевой карты-СУЩЕСТВА («Проходная на свалку»:
    // взял существо → сбросил → «призовите копию»). Идентичность — CardModelComponent цели; сама цель НЕ
    // двигается (она может быть уже в кладбище после сброса) — создаётся НОВАЯ сущность на свободной клетке
    // фронта владельца (SpawnToBoard, как TransformEffect). Цель не существо → skip (ветвление существо/спелл
    // делают фильтры по TriggerSubject, это лишь страховка). СИНК ДАРОМ: generate ре-ранится на обоих
    // клиентах, ключ детерминирован (NextKey).
    [Serializable]
    public sealed class SummonCopyOfTargetEffect : EffectBase
    {
        public override Game.Core.Shared.Interface.AiEffectRole AiRole => Game.Core.Shared.Interface.AiEffectRole.Summon;
        public int Count = 1;

        [Tooltip("Эффекты к КАЖДОЙ призванной копии (бафф/таймер смерти). Применяются при материализации.")]
        [SerializeReference] public List<IEffect> Modifiers = new();

        public override void Init(EcsWorld world, int cardEntity, int playerEntity)
        {
            base.Init(world, cardEntity, playerEntity);
            if (Modifiers != null) foreach (var mod in Modifiers) mod?.Init(world, cardEntity, playerEntity);
        }

        public override void Dispose()
        {
            base.Dispose();
            if (Modifiers != null) foreach (var mod in Modifiers) mod?.Dispose();
        }

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (target < 0) return;
            var pool = world.GetPool<CardModelComponent>();
            if (!pool.Has(target)) return;
            ref var m = ref pool.Get(target);
            if (m.CardType != Game.Core.Service.EnumService.CardType.Creature) return;   // копируем только существ

            var ownerPool = world.GetPool<OwnerComponent>();
            if (!ownerPool.Has(cardEntity)) return;
            int ownerId = ownerPool.Get(cardEntity).OwnerId;

            var mods = (Modifiers != null && Modifiers.Count > 0) ? Modifiers : null;
            int n = Count <= 0 ? 1 : Count;
            for (int i = 0; i < n; i++)
            {
                int col = BoardFrontRow.ClaimFreeCell(world, ownerId);   // резерв: мульти-копии не сядут на одну клетку
                if (col < 0) return;                                     // фронт полон
                GenerateCardEffect.SpawnToBoard(world, cardEntity, m.ExpansionId, m.ModelId, BoardFrontRow.FrontRow, col, mods);
            }
        }
    }

    // === class (OOP) === РАЗЫГРАТЬ (бесплатно, авто-кастом) КОПИЮ целевой карты («Подписка на утилизацию»:
    // сбросили карту → «разыграйте копию»; таргетинг TriggerSubject от OnOwnerDiscardArmedTrigger). Отличие
    // от PlayTargetCardEffect: играется НЕ сама цель (она в кладбище и должна там остаться), а СВЕЖАЯ копия
    // по её идентичности. Spawn{autoCast=true}: копия создаётся и тут же кастуется free (AutoCastComponent →
    // роутер, как Фокус-покус; Selected-цели форсятся в Random — окна выбора не будет). СИНК: генерация
    // ре-ранится на обоих клиентах (детерм. ключ), сам каст делает актив (AutoCastSystem гейтит
    // IsLocalActive), пассив получает обычным каст-синком по тому же ключу.
    [Serializable]
    public sealed class PlayCopyOfTargetEffect : EffectBase
    {
        [Tooltip("Всегда форсить случайные цели у разыгранной копии (как Йогг). false → форс только когда " +
                 "розыгрыш идёт НЕ от OnCast игрока (семантика как у PlayTargetCardEffect).")]
        public bool ForceRandomTarget = false;

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (target < 0) return;
            var pool = world.GetPool<CardModelComponent>();
            if (!pool.Has(target)) return;
            ref var m = ref pool.Get(target);
            // Как PlayTargetCardEffect: от чужого триггера (OnDiscard и т.п.) игрок физически не выбирает
            // цели копии — форсим random; от OnCast (сам кастует источник) выбор остаётся игроку.
            bool forceRandom = ForceRandomTarget || !AbilityResolveContext.IsSelfTrigger;
            GenerateCardEffect.Spawn(world, cardEntity, m.ExpansionId, m.ModelId, toHand: false, autoCast: true, forceRandomTarget: forceRandom);
        }
    }

    // === class (OOP) === Положить в РУКУ владельца источника КОПИЮ целевой карты («Проходная на свалку»:
    // взял заклинание → сбросил → «копию в руку, она стоит на 2 меньше»). Идентичность — CardModelComponent
    // цели (зона цели не важна — копия создаётся с нуля). Скидка/баффы — через Modifiers при материализации:
    // «на 2 дешевле» = AddBuffEffect{Buff=BuffCost{Delta=-2, Permanent=true}} (перм. — переживает зоны).
    // СИНК ДАРОМ: как у ShuffleCopiesOfTargetEffect (ре-ран на обоих, детерм. ключи, GeneratedModScratch).
    [Serializable]
    public sealed class CreateCopyOfTargetToHandEffect : EffectBase
    {
        public override Game.Core.Shared.Interface.AiEffectRole AiRole => Game.Core.Shared.Interface.AiEffectRole.Draw;
        public int Count = 1;

        [Tooltip("Эффекты к созданной копии. «Стоит на 2 меньше» = AddBuffEffect{Buff=BuffCost{Delta=-2, Permanent=true}}.")]
        [SerializeReference] public List<IEffect> Modifiers = new();

        public override void Init(EcsWorld world, int cardEntity, int playerEntity)
        {
            base.Init(world, cardEntity, playerEntity);
            if (Modifiers != null) foreach (var mod in Modifiers) mod?.Init(world, cardEntity, playerEntity);
        }

        public override void Dispose()
        {
            base.Dispose();
            if (Modifiers != null) foreach (var mod in Modifiers) mod?.Dispose();
        }

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (target < 0) return;
            var pool = world.GetPool<CardModelComponent>();
            if (!pool.Has(target)) return;
            ref var m = ref pool.Get(target);

            var mods = (Modifiers != null && Modifiers.Count > 0) ? Modifiers : null;
            int n = Count <= 0 ? 1 : Count;
            for (int i = 0; i < n; i++)
                GenerateCardEffect.Spawn(world, cardEntity, m.ExpansionId, m.ModelId, toHand: true, modifiers: mods);
        }
    }

    // === class (OOP) === Замешать в КОЛОДУ владельца источника КОПИЮ целевой карты («Королевский шут»,
    // «Повторная шутка»: «выберите карту в руке — замешайте копию в колоду»). Идентичность — CardModelComponent
    // цели; сама цель остаётся на месте (в отличие от StealToHandEffect и др. — тут именно КОПИЯ, оригинал
    // никуда не девается). Иначе — точный близнец CreateCopyOfTargetToHandEffect (toHand: false вместо true).
    [Serializable]
    public sealed class CreateCopyOfTargetToDeckEffect : EffectBase
    {
        public override Game.Core.Shared.Interface.AiEffectRole AiRole => Game.Core.Shared.Interface.AiEffectRole.Draw;
        public int Count = 1;

        [Tooltip("Эффекты к созданной копии при материализации (см. CreateCopyOfTargetToHandEffect).")]
        [SerializeReference] public List<IEffect> Modifiers = new();

        public override void Init(EcsWorld world, int cardEntity, int playerEntity)
        {
            base.Init(world, cardEntity, playerEntity);
            if (Modifiers != null) foreach (var mod in Modifiers) mod?.Init(world, cardEntity, playerEntity);
        }

        public override void Dispose()
        {
            base.Dispose();
            if (Modifiers != null) foreach (var mod in Modifiers) mod?.Dispose();
        }

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (target < 0) return;
            var pool = world.GetPool<CardModelComponent>();
            if (!pool.Has(target)) return;
            ref var m = ref pool.Get(target);

            var mods = (Modifiers != null && Modifiers.Count > 0) ? Modifiers : null;
            int n = Count <= 0 ? 1 : Count;
            for (int i = 0; i < n; i++)
                GenerateCardEffect.Spawn(world, cardEntity, m.ExpansionId, m.ModelId, toHand: false, modifiers: mods);
        }
    }

    // === class (OOP) === Дать Count случайных карт из ПУЛА в руку владельца. Пул — ассеты
    // CardInstanceData (ICreatable), напр. все жёлтые спеллы. «Сколько раз» в зависимости от контекста
    // (число убитых и т.п.) — НЕ здесь: оборачивай в RepeatEffect (универсально для любого эффекта).
    // СИНК: случайный выбор недетерминирован → актив роллит+пишет в GeneratedCardChannel.Sent (→ снапшот),
    // пассив воспроизводит из него (внутри цепочки; см. RunChainSystem/ApplyChainStage).
    [Serializable]
    public sealed class GainRandomCardEffect : EffectBase
    {
        public override Game.Core.Shared.Interface.AiEffectRole AiRole => Game.Core.Shared.Interface.AiEffectRole.Draw;
        public int Count = 1;

        [Tooltip("Куда положить: false = в руку (по умолч.), true = ВТАСОВАТЬ В КОЛОДУ (Латентный работяга: случайного работягу в колоду).")]
        public bool ToDeck = false;

        [Tooltip("Ассет CardPool (по критериям). Если задан — берём из него, иначе из ручного Pool ниже.")]
        public ScriptableObject PoolAsset;
        [Tooltip("Ручной пул ассетов CardInstanceData (если PoolAsset не задан).")]
        public List<ScriptableObject> Pool = new();

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            for (int i = 0; i < Count; i++)
            {
                string exp;
                int cardId;

                // Пассив: берём переданный активом выбор (детерминизм синка). Актив: ролл из пула + запись.
                if (GeneratedCardChannel.TryReplay(out exp, out cardId))
                {
                    // используем присланную идентичность
                }
                else
                {
                    var pick = PoolUtil.Pick(PoolAsset, Pool);
                    if (pick == null) continue;
                    exp = pick.ExpansionId; cardId = pick.CardId;
                    GeneratedCardChannel.Record(exp, cardId);
                }

                // toHand:false → колода (втасовка по детерм. ключу, DeckShuffleUtil — синхронно на обоих).
                GenerateCardEffect.Spawn(world, cardEntity, exp, cardId, toHand: !ToDeck);
            }
        }
    }

    // === helper === выбор случайной ICreatable из пула: приоритет CardPool-ассета (ICardPool), иначе ручной список.
    internal static class PoolUtil
    {
        public static ICreatable Pick(ScriptableObject poolAsset, List<ScriptableObject> manual)
        {
            if (poolAsset is ICardPool cp && cp.Cards != null && cp.Cards.Count > 0)
                return cp.Cards[UnityEngine.Random.Range(0, cp.Cards.Count)];
            if (manual != null && manual.Count > 0)
                return manual[UnityEngine.Random.Range(0, manual.Count)] as ICreatable;
            return null;
        }

        /// <summary>Случайная карта пула СТОИМОСТЬЮ cost («существо за 1», «случайное за X»). Ровно за X нет —
        /// берём ближайшие по |Δцены| (не фуззлимся: «за 100» даст самое дорогое), случайную среди равных.
        /// cost &lt; 0 → без учёта цены (обычный Pick). Цена — печатная (ICreatable.PlayCostAmount).
        /// Общий для призыва (SpawnRandomCardOnBoard) и трансмутаций (TransformRoll).</summary>
        public static ICreatable PickByCost(ScriptableObject poolAsset, List<ScriptableObject> manual, int cost)
        {
            if (cost < 0) return Pick(poolAsset, manual);

            var best = new List<ICreatable>();
            int bestDelta = int.MaxValue;
            void Consider(ICreatable c)
            {
                if (c == null) return;
                int d = System.Math.Abs(c.PlayCostAmount - cost);
                if (d < bestDelta) { bestDelta = d; best.Clear(); }
                if (d == bestDelta) best.Add(c);
            }

            if (poolAsset is ICardPool cp && cp.Cards != null)
                foreach (var c in cp.Cards) Consider(c);
            else if (manual != null)
                foreach (var so in manual) Consider(so as ICreatable);

            return best.Count > 0 ? best[UnityEngine.Random.Range(0, best.Count)] : null;
        }
    }

    // === class (OOP) === РАЗЫГРАТЬ одну случайную карту из ПУЛА (Фокус-покус). Создаёт карту с AutoCast →
    // AutoCastSystem форс-кастит её (Free) у активного. Таргетинг порождённой карты: интерактивный (игрок сам
    // выбирает цель), ЕСЛИ сам Фокус-покус разыгрывается от OnCast (игрок играет его сейчас, интерактивный
    // контекст есть) — иначе (любой другой триггер) форсится случайный, как Йогг-Сарон (ForceRandomTargeting);
    // ForceRandomTarget=true форсит случайный ВСЕГДА, даже от OnCast. «N штук» = обернуть в RepeatEffect
    // (Фокус-покус → Fixed=2). СИНК: ролл идёт в GeneratedCardChannel (→ снапшот Generated*), пассив TryReplay
    // создаёт ту же карту (детерм. ключ); каст синкается обычными ActionCastData/ActionAbilityData (таргетинг-
    // выбор едет в ключах целей).
    [Serializable]
    public sealed class PlayRandomFromPoolEffect : EffectBase
    {
        [Tooltip("Ассет CardPool (по критериям). Если задан — берём из него, иначе из ручного Pool ниже.")]
        public ScriptableObject PoolAsset;
        [Tooltip("Ручной пул ассетов CardInstanceData (если PoolAsset не задан).")]
        public List<ScriptableObject> Pool = new();

        [Tooltip("Всегда форсить случайную цель у порождённой карты (как Йогг-Сарон), даже если сам источник " +
                 "разыгрывается через OnCast (где по умолчанию цель выбирает игрок).")]
        public bool ForceRandomTarget = false;

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            string exp; int cardId;
            if (GeneratedCardChannel.TryReplay(out exp, out cardId))
            {
                // пассив: присланная активом идентичность
            }
            else
            {
                var pick = PoolUtil.Pick(PoolAsset, Pool);
                if (pick == null) return;
                exp = pick.ExpansionId; cardId = pick.CardId;
                GeneratedCardChannel.Record(exp, cardId);
            }
            // Интерактивный выбор цели допустим только от OnCast (сам Фокус-покус разыгрывается игроком сейчас);
            // любой другой триггер — не в интерактивном контексте (может сработать и в чужой ход) → форс random.
            bool forceRandom = ForceRandomTarget || !AbilityResolveContext.IsSelfTrigger;
            GenerateCardEffect.Spawn(world, cardEntity, exp, cardId, toHand: true, autoCast: true, forceRandomTarget: forceRandom);
        }
    }

    // === class (OOP) === Экзотик «разыграть ЗАНОВО все заклинания матча»: за КАЖДЫЙ розыгрыш спелла
    // владельцем (журнал MatchCounterComponent.SpellsPlayedLog, с повторами — 2×Позвать рой = 2 копии)
    // создаётся КОПИЯ и авто-кастуется Free (AutoCastComponent). Таргетинг копий: интерактивный, ЕСЛИ сам
    // экзотик разыгрывается от OnCast (игрок играет его сейчас) — иначе (любой другой триггер) форсится
    // случайный, как Йогг-Сарон (ForceRandomTarget=true форсит всегда, даже от OnCast). Сам источник
    // исключается по ModelId (иначе рекурсия). СИНК ДАРОМ: журнал зеркален, порядок детерминирован, ключи
    // копий детерминированы (NextKey) → оба клиента создают те же копии; касты копий едут обычными
    // ActionCastData/ActionAbilityData от активного. NB: копии, разыгравшись, сами попадут в журнал —
    // повторный экзотик реплеит и их (по дизайну).
    [Serializable]
    public sealed class ReplayAllSpellsEffect : EffectBase
    {
        [Tooltip("Всегда форсить случайную цель у разыгранных копий (как Йогг-Сарон), даже если сам источник " +
                 "разыгрывается через OnCast (где по умолчанию цель выбирает игрок).")]
        public bool ForceRandomTarget = false;

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            var counters = world.GetPool<MatchCounterComponent>();
            if (PlayerEntity < 0 || !counters.Has(PlayerEntity)) return;
            var log = counters.Get(PlayerEntity).SpellsPlayedLog;
            if (log == null || log.Count == 0) return;

            var modelPool = world.GetPool<CardModelComponent>();
            int selfModel = modelPool.Has(cardEntity) ? modelPool.Get(cardEntity).ModelId : -1;

            bool forceRandom = ForceRandomTarget || !AbilityResolveContext.IsSelfTrigger;

            // Снапшот: касты копий будут дописывать журнал — итерируем зафиксированный список.
            var snapshot = log.ToArray();
            foreach (var rec in snapshot)
            {
                if (rec.ModelId == selfModel) continue;   // не реплеим сам экзотик (рекурсия)
                GenerateCardEffect.Spawn(world, cardEntity, rec.ExpansionId, rec.ModelId, toHand: true, autoCast: true, forceRandomTarget: forceRandom);
            }
        }
    }

    // === class (OOP) === ПРИЗВАТЬ ЗАНОВО все чары, разыгранные владельцем в этом матче (Мистер Постоянство).
    // Журнал — MatchCounterComponent.CharmsPlayedLog (порядок розыгрыша, с повторами, БЕЗ токенов — трекер
    // не пишет их). Каждая чара пересоздаётся на борде свежей копией (SpawnToBoard, Row=-1 — чары без клетки),
    // срок жизни/поведение — заново с инита её модели. ЛИМИТ 5 ЧАР ОБХОДИТСЯ ШТАТНО: лимит проверяет только
    // каст-роутер (RunCastRouterSystem, pre-cost на RequestCardCastEvent) — прямой призыв через него не идёт.
    // СИНК ДАРОМ: журнал зеркален, ключи копий детерминированы (NextKey источника) → оба клиента создают
    // одинаковый набор. Призванные копии НЕ разыгрываются (нет CardCastEvent) → в журнал не дописываются,
    // рекурсии нет.
    [Serializable]
    public sealed class SummonAllPlayedCharmsEffect : EffectBase
    {
        public override Game.Core.Shared.Interface.AiEffectRole AiRole => Game.Core.Shared.Interface.AiEffectRole.Summon;

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            var counters = world.GetPool<MatchCounterComponent>();
            if (PlayerEntity < 0 || !counters.Has(PlayerEntity)) return;
            var log = counters.Get(PlayerEntity).CharmsPlayedLog;
            if (log == null || log.Count == 0) return;

            // Снапшот — на случай если чья-то реакция дополнит журнал во время призыва.
            var snapshot = log.ToArray();
            foreach (var rec in snapshot)
                GenerateCardEffect.SpawnToBoard(world, cardEntity, rec.ExpansionId, rec.ModelId, -1, -1);
        }
    }

    // === class (OOP) === Спелл создаёт ЧАРУ-ТОКЕН на борд (Вуду-будду → постоянная чара-редирект). Charm на
    // борде без клетки (Row=-1). Generate (детерм. ключ, оба клиента ре-ранят). Поведение токена — в его
    // CardCharmModel (TurnsAlive/ReflectDamage), срабатывает на ините → надёжно на обоих.
    [Serializable]
    public sealed class SpawnCharmTokenEffect : EffectBase
    {
        public override Game.Core.Shared.Interface.AiEffectRole AiRole => Game.Core.Shared.Interface.AiEffectRole.Summon;
        [Tooltip("Ассет CardInstanceData чары-токена (перетащить).")]
        public ScriptableObject Source;

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (Source is ICreatable c)
                GenerateCardEffect.SpawnToBoard(world, cardEntity, c.ExpansionId, c.CardId, -1, -1);   // чара без клетки
        }
    }

    // === class (OOP) === Замешать Count карт из ассета в КОЛОДУ ОППОНЕНТА (Старый колдун → 2 вонючих
    // облака; Дать газу — внутри цепочки за каждого погибшего; Гнидальф — обернуть в RepeatEffect{MatchCounter}).
    [Serializable]
    public sealed class GenerateToOpponentDeckEffect : EffectBase
    {
        public override Game.Core.Shared.Interface.AiEffectRole AiRole => Game.Core.Shared.Interface.AiEffectRole.Curse;
        [Tooltip("Ассет CardInstanceData замешиваемой карты (перетащить).")]
        public ScriptableObject Source;
        public int Count = 1;

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (!(Source is ICreatable c)) return;
            int n = Count <= 0 ? 1 : Count;
            for (int i = 0; i < n; i++)
                GenerateCardEffect.SpawnToOpponentDeck(world, cardEntity, c.ExpansionId, c.CardId);
        }
    }
}


