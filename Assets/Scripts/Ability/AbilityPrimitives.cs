using System;
using Game.Core.Ecs.Components;
using Game.Core.Service;
using Leopotam.EcsLite;

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
    // (перм. модификатор = текущему Max), Current += старый Max (cap). RequireDeckColor!=0 → только если в
    // колоде владельца есть карта этого цвета (live-проверка на резолве — на OnMatchStart колода уже собрана).
    [Serializable]
    public sealed class DoubleHealthEffect : EffectBase
    {
        public EnumService.Element RequireDeckColor = 0;   // 0 = без условия

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (target < 0) return;
            if (RequireDeckColor != 0 && !OwnerDeckHasColor(world, RequireDeckColor)) return;

            var pool = world.GetPool<HealthComponent>();
            if (!pool.Has(target)) return;
            ref var h = ref pool.Get(target);
            int old = h.Max;
            h.AddModifier(old, true);                       // Max → 2x (истинно перманентно)
            h.Current = Math.Min(h.Current + old, h.Max);
            EffectUtil.RaiseResource(world, target, EnumService.ResourceType.Health, h.Current, h.Max);
        }

        bool OwnerDeckHasColor(EcsWorld world, EnumService.Element color)
        {
            var deckPool = world.GetPool<DeckComponent>();
            if (!deckPool.Has(PlayerEntity)) return false;
            var deck = deckPool.Get(PlayerEntity).CardEntities;
            if (deck == null) return false;
            var modelPool = world.GetPool<CardModelComponent>();
            foreach (var c in deck)
                if (modelPool.Has(c) && (modelPool.Get(c).Element & color) != 0) return true;
            return false;
        }
    }

    // === class (OOP) === «Похитить» Amount здоровья у цели: урон цели + ПЕРМАНЕНТНО +Max HP получателю на
    // Amount и лечение на столько же. Получатель настраивается (Beneficiary): Owner — игрок-владелец карты
    // (Всадник Голода: OnDie + Random[Enemy] + DrainHealth{2} → твой герой); Self — само существо-источник
    // (вампир, что растёт; не для OnDie — мёртвое). Перманент через HealthComponent.AddModifier(value, true).
    [Serializable]
    public sealed class DrainHealthEffect : EffectBase
    {
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

            int beneficiary = Beneficiary == DrainBeneficiary.Self ? cardEntity : PlayerEntity;
            var hpPool = world.GetPool<HealthComponent>();
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

    // === class (OOP) === УНИВЕРСАЛЬНЫЙ множитель частоты выбранного триггера владельца: триггер Trigger
    // срабатывает Times раз (2 = дважды/Временная петля, 3 = трижды и т.д.). Бампит CastMultiplierService
    // для (владелец, ключ-триггера) на Times-1 → AbilityFire.Mark ставит PendingCasts → N резолвов.
    // Триггер обязан слать свой ключ в Mark (см. TriggerKind/TriggerKeys). Для нескольких триггеров (напр.
    // Временная петля = начало+конец хода) добавь несколько таких эффектов. Temporary=false → до конца матча;
    // Temporary=true → откат после Turns ходов владельца (откат в конце хода: актив — EndTurn Step B, пассив —
    // ReplayEndTurn → CastMultiplierService.TickOwnerTurnEnd, зеркально). СИНК: бамп идёт на обоих (резолв
    // ре-ранится) → сервис одинаков; множитель влияет на число резолвов активного → N ActionAbilityData, пассив реплеит N.
    [Serializable]
    public sealed class SetTriggerMultiplierEffect : EffectBase
    {
        public TriggerKind Trigger = TriggerKind.OnTurnEnd;
        public int Times = 2;        // сколько раз срабатывать (>=1; 1 = без изменений)
        public bool Temporary = false;   // true → откатится после Turns ходов владельца; false → до конца матча
        public int Turns = 1;        // для Temporary: сколько ходов владельца держится (1 = до конца этого хода)

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (Times <= 1) return;
            var ownerPool = world.GetPool<OwnerComponent>();
            if (!ownerPool.Has(cardEntity)) return;
            int ownerId = ownerPool.Get(cardEntity).OwnerId;
            string key = TriggerKeys.Of(Trigger);

            if (Temporary) CastMultiplierService.AddTemporary(ownerId, key, Times - 1, Turns);
            else           CastMultiplierService.Add(ownerId, key, Times - 1);
        }
    }

    // === class (OOP) === Цель-игрок теряет Amount золота (Штраф: AbilityToField на обоих игроков).
    // В отличие от GainGoldEffect (всегда владелец), бьёт по ЦЕЛИ → годится и для оппонента.
    [Serializable]
    public sealed class LoseGoldEffect : EffectBase
    {
        public int Amount = 1;

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            var pool = world.GetPool<GoldComponent>();
            if (!pool.Has(target)) return;
            ref var g = ref pool.Get(target);
            g.Current = Math.Max(0, g.Current - Amount);
            EffectUtil.RaiseResource(world, target, EnumService.ResourceType.Gold, g.Current, g.Max);
        }
    }
}
