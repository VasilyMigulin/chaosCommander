using System;
using Game.Core.Ecs.Components;
using Game.Core.Service;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;
using UnityEngine;

namespace Game.Core.Ability
{
    // ─────────────────────────────────────────────────────────────────────────
    // КЛАСТЕР 1 — примитивы добивания/ресурсов. Уже были в AbilityEffects.cs: DestroyEffect (уничтожить
    // цель-существо), DrawCardEffect (добор владельцу), GainGoldEffect (+золото владельцу). Урон игроку/
    // обоим — композиция DealDamageEffect + NonTarget/Field-таргетинг (у игрока есть HealthComponent).
    // Здесь только реально недостающее. СИНК: детерминированы по (caster, target) → пассив реплеит.
    // ─────────────────────────────────────────────────────────────────────────

    // === class (OOP) === Уничтожить СЕБЯ (Всадники: «при разыгрывании погибает»). NonTarget; его OnDie
    // (предсмертный эффект) сработает следом штатно через DieSystem.
    [Serializable]
    public sealed class SelfDestructEffect : EffectBase
    {
        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            var hp = world.GetPool<HealthComponent>();

            if (hp.Has(cardEntity))
            {
                ref var h = ref hp.Get(cardEntity); 
                h.Current = 0; 
            }

            var dead = world.GetPool<DeadTag>();

            if (!dead.Has(cardEntity)) 
                dead.Add(cardEntity);
        }
    }

    // === class (OOP) === Удвоить здоровье цели-игрока (Проповедник, NonTarget → свой игрок). Max → 2x
    // (перм. модификатор = текущему Max), Current += старый Max (cap). Условие «все карты жёлтые» —
    // НЕ здесь: штатный ConditionRoot → AllCardsHaveColorCondition (старый вшитый RequireDeckColor удалён —
    // он к тому же проверял «есть хотя бы одна цветная в колоде», а не «все, включая руку»).
    [Serializable]
    public sealed class DoubleHealthEffect : EffectBase
    {
        public override Game.Core.Shared.Interface.AiEffectRole AiRole => Game.Core.Shared.Interface.AiEffectRole.Heal;

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (target < 0) return;

            var pool = world.GetPool<HealthComponent>();
            if (!pool.Has(target)) return;
            ref var h = ref pool.Get(target);
            int old = h.Max;
            h.AddModifier(old, true);                       // Max → 2x (истинно перманентно)
            h.Current = Math.Min(h.Current + old, h.Max);
            EffectUtil.RaiseResource(world, target, EnumService.ResourceType.Health, h.Current, h.Max);
        }
    }

    // === class (OOP) === «Похитить» Amount здоровья у цели: цель ПЕРМАНЕНТНО теряет Max HP на Amount (+ обычный
    // урон — смерть/трекинг/редиректы идут штатным TakeDamageEvent, это НЕ трогаем), получатель — ПЕРМАНЕНТНО
    // +Max HP на Amount и лечение на столько же. Получатель настраивается (Beneficiary): Owner — игрок-владелец
    // карты (Всадник Голода: OnDie + Random[Enemy] + DrainHealth{2} → твой герой); Self — само существо-источник
    // (вампир, что растёт; не для OnDie — мёртвое). Симметрично: что цель потеряла навсегда, получатель навсегда
    // приобрёл (баг 2026-08-21 — раньше цель теряла только текущее HP, обычным уроном, и могла вылечиться обратно).
    [Serializable]
    public sealed class DrainHealthEffect : EffectBase
    {
        public override Game.Core.Shared.Interface.AiEffectRole AiRole => Game.Core.Shared.Interface.AiEffectRole.Damage;
        /// <summary>Кому идёт похищенное здоровье. Owner — игрок-владелец карты (Всадник Голода → твой герой).
        /// Self — само существо-источник (вампир, что растёт само; НЕ годится для OnDie — оно уже мёртвое).</summary>
        public enum DrainBeneficiary { Owner, Self }

        public int Amount = 2;
        public DrainBeneficiary Beneficiary = DrainBeneficiary.Owner;

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (target < 0) return;

            var dmgPool = world.GetPool<TakeDamageEvent>();
            if (!dmgPool.Has(target)) dmgPool.Add(target);
            ref var d = ref dmgPool.Get(target);
            d.Amount += Amount;
            d.Attacker = cardEntity;

            // Цель ПЕРМАНЕНТНО теряет Max HP (истинное «похищение» — не просто урон, который можно отхилить).
            // RecalculateValue сам клампит Current, если он теперь выше нового Max.
            var hpPool = world.GetPool<HealthComponent>();
            if (hpPool.Has(target))
            {
                ref var th = ref hpPool.Get(target);
                th.AddModifier(-Amount, true);
                if (world.GetPool<PlayerComponent>().Has(target))   // игроку — обновить ресурс-бар; существо обновит CreatureStatsViewSystem
                    EffectUtil.RaiseResource(world, target, EnumService.ResourceType.Health, th.Current, th.Max);
            }

            int beneficiary = Beneficiary == DrainBeneficiary.Self ? cardEntity : PlayerEntity;
            if (beneficiary >= 0 && hpPool.Has(beneficiary))
            {
                ref var h = ref hpPool.Get(beneficiary);
                h.AddModifier(Amount, true);                       // перманентный +Max («похищение»)
                h.Current = Math.Min(h.Current + Amount, h.Max);   // лечим на столько же (теперь влезает)
                if (beneficiary == PlayerEntity)                   // игроку — обновляем ресурс-бар; существо обновит CreatureStatsViewSystem
                    EffectUtil.RaiseResource(world, beneficiary, EnumService.ResourceType.Health, h.Current, h.Max);
            }
        }
    }

    // === class (OOP) === Спелл вешает на игрока-владельца маркер блока/редиректа урона (Вуду-будду).
    // ReflectDamageSystem по нему блокирует входящий урон на ходу владельца; чара-токен редиректит.
    [Serializable]
    public sealed class AddPlayerReflectMarkerEffect : EffectBase
    {
        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            var p = world.GetPool<ReflectDamageComponent>();
            if (!p.Has(PlayerEntity)) p.Add(PlayerEntity);   // владелец-игрок
        }
    }

    // === class (OOP) === Нанести ЦЕЛИ урон = последний полученный владельцем (LastDamageTakenComponent,
    // пишет ReflectDamageSystem при блоке). Для редиректа Вуду: цель выбирает таргетинг —
    // AbilityToTarget{Random, Filters=[EnemyTargetFilter, OpponentTargetFilter]} → случайный враг (существо/
    // оппонент); выбор синкается target-ключом, величина зеркальна (LastDamageTaken). HP владельца НЕ трогаем
    // (уже заблокирован системой). Универсально: можно бить «последним полученным уроном» по любой цели.
    [Serializable]
    public sealed class DealLastDamageEffect : EffectBase
    {
        public override Game.Core.Shared.Interface.AiEffectRole AiRole => Game.Core.Shared.Interface.AiEffectRole.Damage;
        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (target < 0) return;
            var lastPool = world.GetPool<LastDamageTakenComponent>();
            if (!lastPool.Has(PlayerEntity)) return;
            int amount = lastPool.Get(PlayerEntity).Amount;
            if (amount <= 0) return;

            var dmgPool = world.GetPool<TakeDamageEvent>();
            if (!dmgPool.Has(target)) dmgPool.Add(target);
            ref var d = ref dmgPool.Get(target);
            d.Amount += amount;
            d.Attacker = cardEntity;
        }
    }

    // === class (OOP) === Нанести цели урон, равный ТЕКУЩЕМУ стату ИСТОЧНИКА или его ВЛАДЕЛЬЦА (Stat/Source —
    // тот же StatKind/StatSourceEntity/StatCompareTargetFilter.TryRead, что и у RepeatEffect.CountSource.Stat).
    // Attack/Self — «урон = своя атака» (по факту работает только у существ: у карты-заклинания источник —
    // сама карта-спелл, без AttackComponent). Mana/Gold всегда требуют Source=Owner — у карты этих пулов нет
    // физически, только у игрока. Нет нужного компонента на резолвнутой сущности → 0 урона (Apply — no-op).
    // На будущее: если атака когда-нибудь появится у другого источника (напр. у игрока — оружие/герой-атака),
    // эффект заработает сам, без правок — читаем компонент С РЕЗОЛВНУТОЙ сущности, не ветвим по типу карты.
    [Serializable]
    public sealed class DealDamageEqualToStatEffect : EffectBase, IDynamicValue
    {
        public override Game.Core.Shared.Interface.AiEffectRole AiRole => Game.Core.Shared.Interface.AiEffectRole.Damage;

        [Tooltip("Какой стат читаем.")]
        public StatKind Stat = StatKind.Attack;
        [Tooltip("Чей стат — сам источник или его владелец-игрок (обязательно Owner для Mana/Gold).")]
        public StatSourceEntity Source = StatSourceEntity.Self;

        public int DynamicValueCount => 1;
        public int GetDynamicValue(int index, EcsWorld world, int cardEntity, int playerEntity) => ReadStat(world, cardEntity);

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (target < 0 || !world.GetPool<HealthComponent>().Has(target)) return;
            int amount = ReadStat(world, cardEntity);
            if (amount <= 0) return;

            var dmgPool = world.GetPool<TakeDamageEvent>();
            if (!dmgPool.Has(target)) dmgPool.Add(target);
            ref var d = ref dmgPool.Get(target);
            d.Amount += amount;
            d.Attacker = cardEntity;
        }

        int ReadStat(EcsWorld world, int cardEntity)
        {
            int e = StatSourceUtil.Resolve(world, cardEntity, Source);
            return StatCompareTargetFilter.TryRead(world, e, Stat, out int v) ? v : 0;
        }
    }

    // === class (OOP) === УНИВЕРСАЛЬНЫЙ множитель частоты выбранного триггера владельца: триггер Trigger
    // срабатывает Times раз (2 = дважды/Временная петля, 3 = трижды и т.д.). Бампит CastMultiplierService
    // для (владелец, ключ-триггера) → AbilityFire.Mark ставит PendingCasts → N резолвов. Триггер обязан
    // слать свой ключ в Mark (см. TriggerKind/TriggerKeys). Для нескольких триггеров (напр. Временная петля
    // = начало+конец хода) добавь несколько таких эффектов.
    //
    // ТРИ ВЗАИМОИСКЛЮЧАЮЩИХ способа истощения источника (решение пользователя 2026-08-05):
    //   Charges=0, Temporary=false → Permanent: до конца матча.
    //   Charges=0, Temporary=true  → откат после Turns ходов владельца (конец хода: актив — EndTurn Step B,
    //                                 пассив — ReplayEndTurn → CastMultiplierService.TickOwnerTurnEnd).
    //   Charges>0                  → съедаемый (Волшебник Упс): живёт Charges СРАБАТЫВАНИЙ этого ключа
    //                                 (списывается за проц — CastMultiplierService.ConsumeCharge из
    //                                 AbilityFire.Mark, не по ходам); Temporary/Turns в этом случае игнорируются.
    //
    // АРИФМЕТИКА СТЕКА: несколько источников (в т.ч. с другой карты) на один ключ складывают СВОИ ЛИЦА —
    // «дважды»(2) + «трижды»(3) = пять срабатываний, а не «1 + дельты» (см. CastMultiplierService).
    // СИНК: бамп идёт на обоих (резолв ре-ранится) → сервис одинаков; множитель влияет на число резолвов
    // активного → N ActionAbilityData, пассив реплеит N.
    [Serializable]
    public sealed class SetTriggerMultiplierEffect : EffectBase
    {
        public TriggerKind Trigger = TriggerKind.OnTurnEnd;

        [UnityEngine.Tooltip("Какие карты множить: Any = все (Временная петля), Spell = только заклинания («спеллы срабатывают дважды»), Creature/Charm. Источники на Any и на конкретный тип складываются.")]
        public MultiplierCardScope CardScope = MultiplierCardScope.Any;   // default 0 → старые ассеты (Петля) читаются как Any

        public int Times = 2;        // лицо множителя: сколько раз срабатывать (>=2; 1 = без изменений)

        [UnityEngine.Tooltip("Съедаемый источник: сколько СРАБАТЫВАНИЙ переживёт (списывается за проц). >0 — Temporary/Turns ниже игнорируются (Волшебник Упс).")]
        public int Charges = 0;

        public bool Temporary = false;   // true → откатится после Turns ходов владельца; false → до конца матча
        public int Turns = 1;        // для Temporary: сколько ходов владельца держится (1 = до конца этого хода)

        // Условия («если в колоде нет существ» и т.п.) — НЕ флагами здесь, а штатным ConditionRoot
        // (EffectBase): экзотик = ConditionRoot → NoCreaturesInDeckCondition. Резолв гейтится IsReady.

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (Times <= 1) return;
            var ownerPool = world.GetPool<OwnerComponent>();
            if (!ownerPool.Has(cardEntity)) return;
            int ownerId = ownerPool.Get(cardEntity).OwnerId;

            string key = CastMultiplierService.ComposeKey(TriggerKeys.ScopeKey(CardScope), TriggerKeys.Of(Trigger));

            if (Charges > 0)       CastMultiplierService.AddCharges(ownerId, key, Times, Charges);
            else if (Temporary)    CastMultiplierService.AddTemporary(ownerId, key, Times, Turns);
            else                   CastMultiplierService.Add(ownerId, key, Times);
        }
    }

    // === class (OOP) === Цель-игрок теряет Amount золота (карта 53 «оба теряют»: AbilityToField на обоих
    // игроков). В отличие от GainGoldEffect (всегда владелец), бьёт по ЦЕЛИ → годится и для оппонента.
    // Отнимаем МАКСИМУМ (кламп 0), не Current: старт хода делает Current = Max (RunTurnStartSystem),
    // поэтому потеря Current для НЕактивного игрока испарялась при переходе хода — эффект был осмыслен
    // только против кастера. Удар по Max — честная потеря дохода для обоих: запас отрастает обратно
    // обычным приростом (+1..N/ход, кап 10). Current заодно клампится к новому Max.
    [Serializable]
    public sealed class LoseGoldEffect : EffectBase
    {
        public override Game.Core.Shared.Interface.AiEffectRole AiRole => Game.Core.Shared.Interface.AiEffectRole.Curse;
        public int Amount = 1;

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            var pool = world.GetPool<GoldComponent>();
            if (!pool.Has(target)) return;
            ref var g = ref pool.Get(target);
            g.Max     = Math.Max(0, g.Max - Amount);
            g.Current = Math.Min(g.Current, g.Max);
            EffectUtil.RaiseResource(world, target, EnumService.ResourceType.Gold, g.Current, g.Max);
        }
    }
}
