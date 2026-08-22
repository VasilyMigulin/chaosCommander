using System;
using System.Collections.Generic;
using Game.Core.Ecs.Components;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Game.Core.Ability
{
    // ─────────────────────────────────────────────────────────────────────────
    // СОЗДАНИЕ существ НА БОРД — семейство GENERATE (CreateCardEvent{InBoard}; оба клиента ре-ранят Apply
    // с ДЕТЕРМИНИРОВАННЫМИ ключами и одинаково считают свободные клетки на зеркальной доске → синк без
    // спец-канала). Клетку каждый спавн БЕРЁТ через BoardFrontRow.ClaimFreeCell (резерв на резолв) — поэтому
    // одиночные эффекты КОРРЕКТНО работают под RepeatEffect (не садятся на одну клетку). «Сколько раз» —
    // ВСЕГДА через RepeatEffect (универсально); отдельных count-эффектов нет. FillRow — отдельный смысл
    // «весь ряд» (MaxCount<=0), его RepeatEffect не выражает.
    // NB: InBoard-создание не вызывает «при разыгрывании» токена (см. GenerateCardEffect.SpawnToBoard).
    // ─────────────────────────────────────────────────────────────────────────

    // === base === Общее для всех «создать существо на борде»: модификаторы порождённого (аналог
    // SummonEffect.SummonModifiers у summon-семейства). Применяются к КАЖДОЙ порождённой сущности
    // (target = порождённый, cardEntity = карта-источник) ПОСЛЕ материализации: сущности в момент Apply
    // ещё нет (CreateCardEvent отложенный) → едут через GeneratedModScratch, применяет CreateCardSystem.
    // Любой target-эффект: DeathTimerEffect{3} («умрут через 3 хода» — Заполонить сорняками),
    // BuffStatsEffect{+1/+1} и т.п. СИНК ДАРОМ: generate ре-ранится на обоих клиентах.
    public abstract class SpawnOnBoardEffect : EffectBase
    {
        public override Game.Core.Shared.Interface.AiEffectRole AiRole => Game.Core.Shared.Interface.AiEffectRole.Summon;

        [Tooltip("Модификаторы порождённого существа (умрёт через N, +X/+X, ...). Пусто → просто создание.")]
        [SerializeReference] public List<IEffect> SummonModifiers = new();

        [Tooltip("Свой эффект ПОЯВЛЕНИЯ у самой способности призыва (портал/аура/финальный аккорд) — " +
                 "ЗАМЕНЯЕТ и встроенный SummonVfx карты-модели порождённого существа, и дефолтный фолбэк " +
                 "(DefaultAbilityVfxConfig.DefaultSummonVfxPrefab). Пусто (ни одной фазы) — существо " +
                 "появляется как обычно (свой SummonVfx / дефолт).")]
        public Game.Core.Shared.Interface.SummonVfxSpec AbilitySummonVfx;

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

        // Модификаторы, реально идущие в GenerateCardEffect.SpawnToBoard: SummonModifiers + (если задан)
        // неявный оверрайд SummonVfxComponent порождённой сущности спекой AbilitySummonVfx. Оверрайд НЕ
        // авторится как элемент SummonModifiers — добавляется автоматически, последним (гарантированно
        // перезаписывает то, что успела выставить CardCreatureModel.OnInit до применения модификаторов).
        protected List<IEffect> EffectiveModifiers()
        {
            if (AbilitySummonVfx == null || !AbilitySummonVfx.HasAny) return SummonModifiers;
            var list = new List<IEffect>(SummonModifiers ?? new List<IEffect>()) { new OverrideSummonVfxEffect(AbilitySummonVfx) };
            return list;
        }
    }

    // === class (OOP) === Служебный модификатор (не авторится в инспекторе, см. SpawnOnBoardEffect.
    // EffectiveModifiers): ставит ОДНОРАЗОВЫЙ AbilitySummonVfxOverrideComponent на порождённую сущность —
    // SpawnCreatureViewSystem прочтёт и СРАЗУ УДАЛИТ его при первом спауне вида. НЕ трогаем сам
    // SummonVfxComponent (интринзик существа) — иначе оверрайд пережил бы баунс/воскрешение (те снимают
    // только ViewSpawnedTag) и подменял бы вид существу навсегда даже при возврате на стол НЕ через эту
    // способность.
    sealed class OverrideSummonVfxEffect : IEffect
    {
        readonly Game.Core.Shared.Interface.SummonVfxSpec _spec;
        public OverrideSummonVfxEffect(Game.Core.Shared.Interface.SummonVfxSpec spec) => _spec = spec;

        public void Init(EcsWorld world, int cardEntity, int playerEntity) { }
        public void Dispose() { }
        public bool IsReady => true;

        public void Apply(EcsWorld world, int cardEntity, int target)
        {
            var pool = world.GetPool<AbilitySummonVfxOverrideComponent>();
            if (pool.Has(target)) pool.Get(target).Spec = _spec;
            else pool.Add(target).Spec = _spec;
        }
    }

    // === class (OOP) === Создать ОДИН токен из ассета на свободной клетке фронта. «N токенов» = обернуть в
    // RepeatEffect (Кладка/Токсичная мать → Fixed; Позвать рой → MatchPlayedSelf; Грыз-стиль → архетип).
    [Serializable]
    [MovedFrom(true, sourceClassName: "SummonTokenEffect")]   // нейминг: создаёт КАРТУ (токенность = IsToken ассета)
    public sealed class SpawnCardOnBoardEffect : SpawnOnBoardEffect
    {
        [Tooltip("Ассет CardInstanceData карты (любой; токен = флаг IsToken ассета).")]
        public ScriptableObject Source;

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (!(Source is ICreatable c)) return;
            var ownerPool = world.GetPool<OwnerComponent>();
            if (!ownerPool.Has(cardEntity)) return;
            int ownerId = ownerPool.Get(cardEntity).OwnerId;

            int col = BoardFrontRow.ClaimFreeCell(world, ownerId);
            if (col < 0) return;   // фронт полон
            GenerateCardEffect.SpawnToBoard(world, cardEntity, c.ExpansionId, c.CardId, BoardFrontRow.FrontRow, col, EffectiveModifiers());
        }
    }

    // === class (OOP) === Создать ОДИН СЛУЧАЙНЫЙ токен из пула на свободной клетке фронта (Фокус-покус через
    // RepeatEffect{Fixed=2}). Как GainRandomCardEffect, но на борд: АКТИВ роллит UnityEngine.Random + Record
    // в GeneratedCardChannel (едет в снапшоте способности), ПАССИВ берёт присланное (TryReplay) → та же карта.
    // Клетку резервируем ДО ролла — если фронт полон, не роллим (иначе канал бы рассинхронился).
    //
    // «ЗА X» (FilterByCost): из пула берётся карта СТОИМОСТЬЮ X, где X — ЛЮБОЙ счётчик проекта
    // (RepeatEffect.CountSource через общий AbilityCount.Resolve). «Попаданцы»: CostSource=SelfResolves →
    // 1-я активация призывает за 1, 2-я за 2, 3-я за 3 — «улучшается с каждой активацией». Ровно за X в
    // пуле нет → берётся ближайшее по цене (PoolUtil.PickByCost). Без флага — как раньше, любой из пула
    // (пул курирует автор).
    [Serializable]
    [MovedFrom(true, sourceClassName: "SummonRandomTokenEffect")]
    public sealed class SpawnRandomCardOnBoardEffect : SpawnOnBoardEffect
    {
        [Tooltip("Ассет CardPool (по критериям). Если задан — берём из него, иначе из ручного Pool ниже.")]
        public ScriptableObject PoolAsset;
        [Tooltip("Ручной пул ассетов CardInstanceData (если PoolAsset не задан).")]
        public List<ScriptableObject> Pool = new();

        [Tooltip("Брать из пула карту ЗАДАННОЙ стоимости (иначе — любую случайную).")]
        public bool FilterByCost = false;
        [Tooltip("Откуда берётся стоимость X. Fixed → FixedCost; SelfResolves → номер активации (1,2,3… — " +
                 "«улучшается с каждой активацией», Попаданцы); любой другой счётчик проекта тоже годится.")]
        public RepeatEffect.CountSource CostSource = RepeatEffect.CountSource.Fixed;
        [Tooltip("Стоимость для CostSource=Fixed.")]
        public int FixedCost = 1;
        [Tooltip("Для счётчиков по конкретной карте (MatchPlayedCard и т.п.).")]
        public ScriptableObject CostCountCard;
        [Tooltip("Для счётчика по архетипу (MatchArchetypeInvoked).")]
        [SerializeReference] public ICreatureTag CostArchetype;

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            var ownerPool = world.GetPool<OwnerComponent>();
            if (!ownerPool.Has(cardEntity)) return;
            int ownerId = ownerPool.Get(cardEntity).OwnerId;

            int col = BoardFrontRow.ClaimFreeCell(world, ownerId);
            if (col < 0) return;   // фронт полон — не роллим (синхронно на обоих → канал не съезжает)

            string exp; int cardId;
            if (GeneratedCardChannel.TryReplay(out exp, out cardId))
            {
                // пассив: используем присланную активом идентичность
            }
            else
            {
                // -1 = без учёта цены. Счётчик (SelfResolves и пр.) считает ОБЩИЙ AbilityCount — тот же,
                // что у RepeatEffect, поэтому «за номер активации» ведёт себя как «Нечищенный источник».
                int cost = FilterByCost
                    ? AbilityCount.Resolve(world, PlayerEntity, cardEntity, CostSource, FixedCost, CostCountCard, CostArchetype)
                    : -1;
                var pick = PoolUtil.PickByCost(PoolAsset, Pool, cost);
                if (pick == null) return;
                exp = pick.ExpansionId; cardId = pick.CardId;
                GeneratedCardChannel.Record(exp, cardId);
            }
            GenerateCardEffect.SpawnToBoard(world, cardEntity, exp, cardId, BoardFrontRow.FrontRow, col, EffectiveModifiers());
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ЗАПОЛНЕНИЕ ВСЕГО ряда (Главарь преисподней/Глава сатанистов/Огненная стена) — отдельный смысл «весь
    // фронт» (MaxCount<=0 → все свободные). Это НЕ «N раз» (RepeatEffect), поэтому остаётся отдельным.
    // ─────────────────────────────────────────────────────────────────────────
    public abstract class FillRowEffect : SpawnOnBoardEffect
    {
        [Tooltip("Максимум существ (<=0 → весь свободный фронт-ряд).")]
        public int MaxCount = 0;

        /// <summary>Идентичность создаваемых существ (токен из ассета / копия себя).</summary>
        protected abstract bool TryGetIdentity(EcsWorld world, int cardEntity, out string exp, out int cardId);

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (!TryGetIdentity(world, cardEntity, out string exp, out int id)) return;

            var ownerPool = world.GetPool<OwnerComponent>();
            if (!ownerPool.Has(cardEntity)) return;
            int ownerId = ownerPool.Get(cardEntity).OwnerId;

            var mods = EffectiveModifiers();
            int placed = 0;
            while (MaxCount <= 0 || placed < MaxCount)
            {
                int col = BoardFrontRow.ClaimFreeCell(world, ownerId);
                if (col < 0) break;
                GenerateCardEffect.SpawnToBoard(world, cardEntity, exp, id, BoardFrontRow.FrontRow, col, mods);
                placed++;
            }
        }
    }

    // === class (OOP) === Заполнить фронт-ряд ТОКЕНАМИ из ассета.
    [Serializable]
    [MovedFrom(true, sourceClassName: "FillRowWithTokenEffect")]
    public sealed class FillRowWithCardEffect : FillRowEffect
    {
        [Tooltip("Ассет CardInstanceData карты (любой; токен = флаг IsToken ассета).")]
        public ScriptableObject Source;

        protected override bool TryGetIdentity(EcsWorld world, int cardEntity, out string exp, out int cardId)
        {
            if (Source is ICreatable c) { exp = c.ExpansionId; cardId = c.CardId; return true; }
            exp = null; cardId = -1; return false;
        }
    }

    // === class (OOP) === Заполнить фронт-ряд КОПИЯМИ самого источника (идентичность из CardModelComponent).
    [Serializable]
    public sealed class FillRowWithCopyOfSelfEffect : FillRowEffect
    {
        protected override bool TryGetIdentity(EcsWorld world, int cardEntity, out string exp, out int cardId)
        {
            var pool = world.GetPool<CardModelComponent>();
            if (pool.Has(cardEntity))
            {
                ref var m = ref pool.Get(cardEntity);
                exp = m.ExpansionId; cardId = m.ModelId; return true;
            }
            exp = null; cardId = -1; return false;
        }
    }

    // === class (OOP) === Заполнить РУКУ владельца токенами из ассета (Пиромант → Поджоги). Создаёт столько,
    // сколько свободно до MaxHand. Generate-семейство (CreateCardEvent в руку, детерм. ключ, оба ре-ранят).
    [Serializable]
    [MovedFrom(true, sourceClassName: "FillHandWithTokenEffect")]
    public sealed class FillHandWithCardEffect : EffectBase
    {
        public ScriptableObject Source;                              // ассет токена (ICreatable)
        public int MaxHand = HandComponent.MaxNonCommanderCards;

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (!(Source is ICreatable c)) return;

            var handPool = world.GetPool<HandComponent>();
            int current = handPool.Has(PlayerEntity) ? handPool.Get(PlayerEntity).Count : 0;
            int slots = MaxHand - current;
            for (int i = 0; i < slots; i++)
                GenerateCardEffect.Spawn(world, cardEntity, c.ExpansionId, c.CardId, toHand: true);
        }
    }
}
