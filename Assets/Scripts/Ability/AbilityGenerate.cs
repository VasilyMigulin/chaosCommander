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

        [Tooltip("Эффекты к КАЖДОЙ порождённой карте при материализации (та же GeneratedModScratch-точка, что " +
                 "у SpawnToBoard/дискавера) — напр. AddAbilityEffect{Granted: AbilityToSelf{OnTurnStart → " +
                 "AddBuffEffect{Permanent}}} для «растущего» токена (Саженцы: 2 Сорняка в руку, каждый +1/+1 " +
                 "в начале хода). Пусто (умолч.) — порождённая карта как есть, старые ассеты без поля читаются так же.")]
        [SerializeReference] public List<IEffect> Modifiers = new();

        /// <summary>Куда кладём порождённую карту.</summary>
        protected abstract Zone TargetZone { get; }

        /// <summary>Идентичность создаваемой карты (из ассета-ICreatable или из самого источника).</summary>
        protected abstract bool TryGetCardIdentity(EcsWorld world, int cardEntity, out string expansionId, out int cardId);

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
            int n = Count <= 0 ? 1 : Count;   // старый ассет без поля Count сериализуется как 0 → создаём 1
            for (int i = 0; i < n; i++)
            {
                if (!TryGetCardIdentity(world, cardEntity, out string exp, out int cardId)) return;
                Spawn(world, cardEntity, exp, cardId, TargetZone == Zone.Hand, modifiers: Modifiers);
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
                    SourceEntity       = sourceCard,        // UI-анимация «замешалось в колоду» летит ОТ карты-источника (см. CardShuffledToDeckEvent)
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

    // === class (OOP) === Призвать ТОЧНУЮ копию целевого существа — как SummonCopyOfTargetEffect (та же
    // печатная карта), но ДОПОЛНИТЕЛЬНО переносит на клон текущее РАНТАЙМ-состояние цели: суммарный бафф
    // статов (Attack/Health/Speed сверх печатной базы, единым перманентным дельта-баффом) и текущие
    // свойства (Taunt/Shielded/Invulnerable/Poisoned/Stealthed/DoubleAttack/Venomous/Retaliate/Vampirism).
    // Снимок цели строится СЕЙЧАС (Apply), а не после материализации клона — GenerateCardEffect асинхронен
    // (CreateCardEvent обрабатывается отложенно), поэтому снимок едет summonModifiers-каналом, тем же, что
    // Modifiers у SummonCopyOfTargetEffect (см. DeepCloneSnapshotEffect ниже).
    //
    // ПОЛНЫЙ клон — переносит и текущий урон (Current HP), не только Max: если у цели 5/8, клон тоже
    // выходит с 5/8, а не с полным баром.
    //
    // Тир (карты с CardModel.Tiers) НЕ трогаем явно, и это НЕ баг: CardTierSystem каждый кадр пересчитывает
    // текущий уровень по TierSource (GoldMax/ManaMax/Corpses — счётчик ВЛАДЕЛЬЦА, не сущности) и пишет
    // статы уровня В БАЗУ (SetBase/SetBaseMax), а не в Modifiers. У клона тот же владелец → та же система
    // за тот же кадр-два сама подтянет его на идентичный уровень — копировать нечего, это не бафф-дельта,
    // а отдельный, ортогональный слой (Base), который наш _attackDelta/_healthDelta/_speedDelta (Value-Base
    // на МОМЕНТ снимка, когда Base у цели уже тир-скорректирован) не трогает и не задваивает.
    //
    // ЧЕГО НЕ переносит (сознательно):
    //   • Роль ИСТОЧНИКА чужих аур (TrackedBuffs/AppliedBuffs как SOURCE) — это исходящие ауры цели на
    //     ДРУГИХ существ, а не баффы НА ней самой. Баффы, полученные целью КАК цель (в т.ч. от чужого
    //     Tracked-источника — «выберите существо, оно получает +1 к здоровью»), уже сидят в её текущих
    //     Attack.Value/Health.Max/Speed.Max, поэтому дельта-снимок выше их и так переносит.
    [Serializable]
    public sealed class SummonDeepCloneEffect : EffectBase
    {
        public override Game.Core.Shared.Interface.AiEffectRole AiRole => Game.Core.Shared.Interface.AiEffectRole.Summon;
        public int Count = 1;

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (target < 0) return;
            var pool = world.GetPool<CardModelComponent>();
            if (!pool.Has(target)) return;
            ref var m = ref pool.Get(target);
            if (m.CardType != Game.Core.Service.EnumService.CardType.Creature) return;   // клонируем только существ

            var ownerPool = world.GetPool<OwnerComponent>();
            if (!ownerPool.Has(cardEntity)) return;
            int ownerId = ownerPool.Get(cardEntity).OwnerId;

            var snapshot = new IEffect[] { DeepCloneSnapshotEffect.Capture(world, target) };

            int n = Count <= 0 ? 1 : Count;
            for (int i = 0; i < n; i++)
            {
                int col = BoardFrontRow.ClaimFreeCell(world, ownerId);   // резерв: мульти-клоны не сядут на одну клетку
                if (col < 0) return;                                     // фронт полон
                GenerateCardEffect.SpawnToBoard(world, cardEntity, m.ExpansionId, m.ModelId, BoardFrontRow.FrontRow, col, snapshot);
            }
        }
    }

    // === helper === Снимок рантайм-состояния цели для SummonDeepCloneEffect — НЕ author-facing эффект
    // (не [Serializable], в SerializeReference-дропдауне не появляется): строится программно из ЖИВОЙ
    // цели в момент Apply, а не настраивается в инспекторе. Наследует EffectBase (не голый IEffect) —
    // тот же принцип, что у всех остальных эффектов проекта (иначе молча ломается подсветка AbilityReady).
    sealed class DeepCloneSnapshotEffect : EffectBase
    {
        int _attackDelta, _healthDelta, _speedDelta;
        bool _hasHealth; int _currentHealth;
        bool _taunt, _invulnerable, _doubleAttack, _retaliate, _vampirism;
        bool _shielded, _stealthed;
        int _shieldCharges, _poisonStacks, _stealthTurns, _venomousStacks;

        public static DeepCloneSnapshotEffect Capture(EcsWorld world, int target)
        {
            var snap = new DeepCloneSnapshotEffect();

            var atk = world.GetPool<AttackComponent>();
            if (atk.Has(target)) { ref var a = ref atk.Get(target); snap._attackDelta = a.Value - a.Base; }
            var hp = world.GetPool<HealthComponent>();
            if (hp.Has(target)) { ref var h = ref hp.Get(target); snap._healthDelta = h.Max - h.BaseMax; snap._hasHealth = true; snap._currentHealth = h.Current; }
            var spd = world.GetPool<SpeedComponent>();
            if (spd.Has(target)) { ref var s = ref spd.Get(target); snap._speedDelta = s.Max - s.BaseMax; }

            snap._taunt        = world.GetPool<TauntTag>().Has(target);
            snap._invulnerable = world.GetPool<InvulnerableTag>().Has(target);
            snap._doubleAttack = world.GetPool<DoubleAttackTag>().Has(target);
            snap._retaliate    = world.GetPool<RetaliateTag>().Has(target);
            snap._vampirism    = world.GetPool<VampirismTag>().Has(target);

            var shield = world.GetPool<ShieldComponent>();
            if (shield.Has(target)) { snap._shielded = true; snap._shieldCharges = shield.Get(target).Charges; }
            var poison = world.GetPool<PoisonComponent>();
            if (poison.Has(target)) snap._poisonStacks = poison.Get(target).Stacks;
            var stealth = world.GetPool<StealthComponent>();
            if (stealth.Has(target)) { snap._stealthed = true; snap._stealthTurns = stealth.Get(target).TurnsRemaining; }
            var venomous = world.GetPool<VenomousComponent>();
            if (venomous.Has(target)) snap._venomousStacks = venomous.Get(target).Stacks;

            return snap;
        }

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (_attackDelta != 0 || _healthDelta != 0 || _speedDelta != 0)
                new BuffStat { Attack = _attackDelta, Health = _healthDelta, Speed = _speedDelta, Permanent = true }
                    .Apply(world, cardEntity, target);

            // ПОЛНЫЙ клон — переносим и текущий урон, не только Max. BuffStat.Apply выше для позитивной
            // Health-дельты уже поднял Current вслед за Max; здесь перезаписываем ТОЧНЫМ снятым значением
            // (не суммируем повторно) — так клон выходит с той же царапиной, что и цель на момент снятия.
            if (_hasHealth)
            {
                var hp = world.GetPool<HealthComponent>();
                if (hp.Has(target))
                {
                    ref var h = ref hp.Get(target);
                    h.Current = _currentHealth > h.Max ? h.Max : _currentHealth;
                    GameEventBus.Publish(new CreatureHealthChangedEvent { CreatureEntity = target });
                }
            }

            if (_taunt)        EnsureTag<TauntTag>(world, target);
            if (_invulnerable) EnsureTag<InvulnerableTag>(world, target);
            if (_doubleAttack) EnsureTag<DoubleAttackTag>(world, target);
            if (_retaliate)    EnsureTag<RetaliateTag>(world, target);
            if (_vampirism)    EnsureTag<VampirismTag>(world, target);

            if (_shielded)
            {
                var p = world.GetPool<ShieldComponent>();
                if (!p.Has(target)) p.Add(target);
                p.Get(target).Charges = _shieldCharges;
            }
            if (_poisonStacks > 0)
            {
                var p = world.GetPool<PoisonComponent>();
                if (!p.Has(target)) p.Add(target);
                p.Get(target).Stacks = _poisonStacks;
            }
            if (_stealthed)
            {
                var p = world.GetPool<StealthComponent>();
                if (!p.Has(target)) p.Add(target);
                p.Get(target).TurnsRemaining = _stealthTurns;
            }
            if (_venomousStacks > 0)
            {
                var p = world.GetPool<VenomousComponent>();
                if (!p.Has(target)) p.Add(target);
                p.Get(target).Stacks = _venomousStacks;
            }
        }

        static void EnsureTag<T>(EcsWorld world, int entity) where T : struct
        {
            var p = world.GetPool<T>();
            if (!p.Has(entity)) p.Add(entity);
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

        [Tooltip("Модификаторы КАЖДОЙ порождённой карты (напр. AddHandDiscardTimerEffect — Сделка с чертом: " +
                 "«сброшены через N ходов»). Пусто → просто в руку/колоду, без довеска.")]
        [SerializeReference] public List<IEffect> Modifiers = new();

        public override void Init(EcsWorld world, int cardEntity, int playerEntity)
        {
            base.Init(world, cardEntity, playerEntity);
            if (Modifiers != null)
                foreach (var mod in Modifiers)
                    mod?.Init(world, cardEntity, playerEntity);
        }

        public override void Dispose()
        {
            base.Dispose();
            if (Modifiers != null)
                foreach (var mod in Modifiers)
                    mod?.Dispose();
        }

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
                GenerateCardEffect.Spawn(world, cardEntity, exp, cardId, toHand: !ToDeck, modifiers: Modifiers);
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
    // выбор едет в ключах целей). Карта играется ПО-НАСТОЯЩЕМУ (CreateCardEvent{InHand}+AutoCast → реальный
    // CardCastEvent на резолве) — в отличие от InBoard-спавна (SpawnCardOnBoardEffect и т.п.), тут работают
    // и OnCastTrigger-эффекты порождённой карты, и любые «когда я разыгран» пассивы на столе.
    //
    // «ЗА X» (FilterByCost, Мерлин-пародия: «разыграйте случайную чару за ТУ ЖЕ стоимость») — тот же
    // PoolUtil.PickByCost, что у SpawnRandomCardOnBoardEffect/«Попаданцы»; X через общий AbilityCount.Resolve.
    // CostSource=Stat + CostStatSource=Target читает стат ЦЕЛИ способности (target параметра Apply — обычно
    // TriggerSubject только что разыгранной владельцем карты, если способность AbilityToTarget{TriggerSubject}).
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

        [Tooltip("Брать из пула карту ЗАДАННОЙ стоимости (иначе — любую случайную).")]
        public bool FilterByCost = false;
        [Tooltip("Откуда берётся стоимость X. Stat+CostStatSource=Target — стоимость ЦЕЛИ способности " +
                 "(TriggerSubject разыгранной карты); любой другой счётчик проекта тоже годится.")]
        public RepeatEffect.CountSource CostSource = RepeatEffect.CountSource.Stat;
        [Tooltip("Стоимость для CostSource=Fixed.")]
        public int FixedCost = 1;
        [Tooltip("Для счётчиков по конкретной карте (MatchPlayedCard и т.п.).")]
        public ScriptableObject CostCountCard;
        [Tooltip("Для счётчика по архетипу (MatchArchetypeInvoked).")]
        [SerializeReference] public ICreatureTag CostArchetype;
        [Tooltip("Для CostSource=Stat: какой стат читаем (обычно Cost).")]
        public StatKind CostStat = StatKind.Cost;
        [Tooltip("Для CostSource=Stat: чья сущность — Self/Owner/Target.")]
        public StatSourceEntity CostStatSource = StatSourceEntity.Self;

        [Tooltip("Форсить TokenTag на разыгранной карте НЕЗАВИСИМО от IsToken её ассета (пул может содержать " +
                 "«настоящие» карты — Мерлин-пародия и т.п.). Токен не уходит на кладбище/не тратит лимит " +
                 "копий, но НЕ обходит рантайм-лимиты вида RunCastRouterSystem.CharmLimit=5 — те считают ЛЮБЫЕ " +
                 "чары под контролем игрока, токен или нет.")]
        public bool MakeToken = false;

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            string exp; int cardId;
            if (GeneratedCardChannel.TryReplay(out exp, out cardId))
            {
                // пассив: присланная активом идентичность
            }
            else
            {
                int cost = FilterByCost
                    ? AbilityCount.Resolve(world, PlayerEntity, cardEntity, CostSource, FixedCost, CostCountCard, CostArchetype, CostStat, CostStatSource, target)
                    : -1;
                var pick = PoolUtil.PickByCost(PoolAsset, Pool, cost);
                if (pick == null) return;
                exp = pick.ExpansionId; cardId = pick.CardId;
                GeneratedCardChannel.Record(exp, cardId);
            }
            // Интерактивный выбор цели допустим только от OnCast (сам Фокус-покус разыгрывается игроком сейчас);
            // любой другой триггер — не в интерактивном контексте (может сработать и в чужой ход) → форс random.
            bool forceRandom = ForceRandomTarget || !AbilityResolveContext.IsSelfTrigger;
            var mods = MakeToken ? ForceTokenModifier : null;
            GenerateCardEffect.Spawn(world, cardEntity, exp, cardId, toHand: true, autoCast: true, forceRandomTarget: forceRandom, modifiers: mods);
        }

        static readonly List<IEffect> ForceTokenModifier = new() { new ForceTokenEffect() };
    }

    // === class (OOP) === Служебный модификатор (не авторится в инспекторе, см. PlayRandomFromPoolEffect.
    // MakeToken): форсит TokenTag на порождённой сущности НЕЗАВИСИМО от IsToken ассета-источника. TokenTag —
    // единственное, от чего зависит вся «токенная» логика движка (не уходит на кладбище — BurnCardSystem/
    // CharmDieSystem/DieSystem, лимбо вместо кладбища при полной руке — RunLeaveBoardSystem, исключение из
    // статистики — PlayerStatsViewSystem) — форсить именно его достаточно, модель/ассет трогать не нужно.
    sealed class ForceTokenEffect : IEffect
    {
        public void Init(EcsWorld world, int cardEntity, int playerEntity) { }
        public void Dispose() { }
        public bool IsReady => true;

        public void Apply(EcsWorld world, int cardEntity, int target)
        {
            var pool = world.GetPool<TokenTag>();
            if (!pool.Has(target)) pool.Add(target);
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
            {
                // Исходник ещё жив и что-то умеет (собранная графтом чара — «Проклятье для принцессы» и
                // подобные) → клонируем его ЖИВЫЕ способности на пересозданную копию, а не только печатный
                // шаблон по ModelId (тот у базового тира пуст — RuntimeAbilities: []). Исходник умер/сгорел —
                // модификатор молча ничего не найдёт (CloneEntityAbilitiesEffect.Apply — Has-гард), копия
                // выйдет как раньше, голым шаблоном.
                IReadOnlyList<IEffect> mods = rec.SourceEntity >= 0
                    ? new IEffect[] { new CloneEntityAbilitiesEffect { SourceEntity = rec.SourceEntity } }
                    : null;
                GenerateCardEffect.SpawnToBoard(world, cardEntity, rec.ExpansionId, rec.ModelId, -1, -1, mods);
            }
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


