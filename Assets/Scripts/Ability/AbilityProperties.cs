using System;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;
using UnityEngine;

namespace Game.Core.Ability
{
    // ─────────────────────────────────────────────────────────────────────────
    // СВОЙСТВА (ICreatureProperty) — новый слой, НЕ Ability. Аналоги кейвордов ХС (Windfury/Divine Shield/...),
    // каждое владеет своим ECS-компонентом. Authored как [SerializeReference] в CardCreatureModel.Properties
    // (печатное свойство карты) ИЛИ раздаётся рантаймом через PropertyBuff{Property} + AddBuffEffect (в т.ч.
    // аурой — Tracked=true, авто-откат при смерти источника, как у любого другого IBuffable).
    // Новый кейворд = новый класс-наследник с уникальным Key — свой компонент/своя логика в системах боя.
    // ─────────────────────────────────────────────────────────────────────────

    [Serializable]
    public sealed class DoubleAttackProperty : ICreatureProperty
    {
        public string Key => "DoubleAttack";

        public void Apply(EcsWorld world, int entity)
        {
            var p = world.GetPool<DoubleAttackTag>();
            if (!p.Has(entity)) p.Add(entity);
        }

        public void Remove(EcsWorld world, int entity)
        {
            var p = world.GetPool<DoubleAttackTag>();
            if (p.Has(entity)) p.Del(entity);
        }

        public bool Has(EcsWorld world, int entity) => world.GetPool<DoubleAttackTag>().Has(entity);
    }

    [Serializable]
    public sealed class ShieldedProperty : ICreatureProperty
    {
        public string Key => "Shielded";

        [Tooltip("Сколько ударов подряд поглощает щит (полностью, без разбивки по урону), прежде чем спадёт.")]
        public int Charges = 3;

        public void Apply(EcsWorld world, int entity)
        {
            var p = world.GetPool<ShieldComponent>();
            if (!p.Has(entity)) p.Add(entity);
            p.Get(entity).Charges = Charges <= 0 ? 1 : Charges;
            GameEventBus.Publish(new CreaturePropertyAuraChangedEvent { CreatureEntity = entity, Key = Key, Active = true });
        }

        public void Remove(EcsWorld world, int entity)
        {
            var p = world.GetPool<ShieldComponent>();
            if (!p.Has(entity)) return;
            p.Del(entity);
            GameEventBus.Publish(new CreaturePropertyAuraChangedEvent { CreatureEntity = entity, Key = Key, Active = false });
        }

        public bool Has(EcsWorld world, int entity)
        {
            var p = world.GetPool<ShieldComponent>();
            return p.Has(entity) && p.Get(entity).Charges > 0;
        }
    }

    [Serializable]
    public sealed class InvulnerableProperty : ICreatureProperty
    {
        public string Key => "Invulnerable";

        public void Apply(EcsWorld world, int entity)
        {
            var p = world.GetPool<InvulnerableTag>();
            if (!p.Has(entity)) p.Add(entity);
        }

        public void Remove(EcsWorld world, int entity)
        {
            var p = world.GetPool<InvulnerableTag>();
            if (p.Has(entity)) p.Del(entity);
        }

        public bool Has(EcsWorld world, int entity) => world.GetPool<InvulnerableTag>().Has(entity);
    }

    [Serializable]
    public sealed class TauntProperty : ICreatureProperty
    {
        public string Key => "Taunt";

        public void Apply(EcsWorld world, int entity)
        {
            var p = world.GetPool<TauntTag>();
            if (!p.Has(entity)) p.Add(entity);
            GameEventBus.Publish(new CreaturePropertyAuraChangedEvent { CreatureEntity = entity, Key = Key, Active = true });
        }

        public void Remove(EcsWorld world, int entity)
        {
            var p = world.GetPool<TauntTag>();
            if (!p.Has(entity)) return;
            p.Del(entity);
            GameEventBus.Publish(new CreaturePropertyAuraChangedEvent { CreatureEntity = entity, Key = Key, Active = false });
        }

        public bool Has(EcsWorld world, int entity) => world.GetPool<TauntTag>().Has(entity);
    }

    // «Ядовитый»/Poisoned — ОДНО свойство: любой урон, который наносит носитель (бой ИЛИ способность, даже
    // полученная позже через бафф), навешивает Stacks стаков статуса «Отравлен» (PoisonComponent) на ЦЕЛЬ —
    // см. TakeDamageSystem.ApplyDamage (единая точка урона, источник урона не важен). Носитель свойства сам
    // от яда не страдает — стаки уходят на того, кого он ударил.
    [Serializable]
    public sealed class PoisonedProperty : ICreatureProperty
    {
        public string Key => "Poisoned";

        [Tooltip("Сколько стаков «Отравлен» получает цель за КАЖДЫЙ нанесённый носителем урон (бой или способность).")]
        public int Stacks = 1;

        public void Apply(EcsWorld world, int entity)
        {
            var p = world.GetPool<VenomousComponent>();
            if (!p.Has(entity)) p.Add(entity);
            p.Get(entity).Stacks = Stacks <= 0 ? 1 : Stacks;
        }

        public void Remove(EcsWorld world, int entity)
        {
            var p = world.GetPool<VenomousComponent>();
            if (p.Has(entity)) p.Del(entity);
        }

        public bool Has(EcsWorld world, int entity) => world.GetPool<VenomousComponent>().Has(entity);
    }

    // «Ответочка» — если носителя АТАКОВАЛИ (бой) и он выжил, отвечает атакующему своим Attack.
    // См. TakeDamageSystem — проверяется сразу после основного удара, урон идёт через ту же ApplyDamage.
    [Serializable]
    public sealed class RetaliateProperty : ICreatureProperty
    {
        public string Key => "Retaliate";

        public void Apply(EcsWorld world, int entity)
        {
            var p = world.GetPool<RetaliateTag>();
            if (!p.Has(entity)) p.Add(entity);
        }

        public void Remove(EcsWorld world, int entity)
        {
            var p = world.GetPool<RetaliateTag>();
            if (p.Has(entity)) p.Del(entity);
        }

        public bool Has(EcsWorld world, int entity) => world.GetPool<RetaliateTag>().Has(entity);
    }

    // «Вампиризм» — любой урон носителя (бой ИЛИ способность) лечит ЕГО ВЛАДЕЛЬЦА (игрока) на ту же
    // величину. См. TakeDamageSystem.ApplyDamage — та же единая точка урона, что у Ядовитого/Ответочки.
    [Serializable]
    public sealed class VampirismProperty : ICreatureProperty
    {
        public string Key => "Vampirism";

        public void Apply(EcsWorld world, int entity)
        {
            var p = world.GetPool<VampirismTag>();
            if (!p.Has(entity)) p.Add(entity);
        }

        public void Remove(EcsWorld world, int entity)
        {
            var p = world.GetPool<VampirismTag>();
            if (p.Has(entity)) p.Del(entity);
        }

        public bool Has(EcsWorld world, int entity) => world.GetPool<VampirismTag>().Has(entity);
    }

    [Serializable]
    public sealed class StealthedProperty : ICreatureProperty
    {
        public string Key => "Stealthed";

        [Tooltip("Сколько ходов ВЛАДЕЛЬЦА носитель исключён из вражеского таргетинга (база 3).")]
        public int Turns = 3;

        public void Apply(EcsWorld world, int entity)
        {
            var p = world.GetPool<StealthComponent>();
            if (!p.Has(entity)) p.Add(entity);
            p.Get(entity).TurnsRemaining = Turns <= 0 ? 1 : Turns;
        }

        public void Remove(EcsWorld world, int entity)
        {
            var p = world.GetPool<StealthComponent>();
            if (p.Has(entity)) p.Del(entity);
        }

        public bool Has(EcsWorld world, int entity) => world.GetPool<StealthComponent>().Has(entity);
    }

    // === buff: свойство ===
    // Мост в единую бафф-абстракцию (AbilityBuffs.cs): любое ICreatureProperty можно выдать через
    // AddBuffEffect{Buff = PropertyBuff{Property}} — разово (Tracked=false) ИЛИ аурой (Tracked=true, напр.
    // будущая лега «ваши токены имеют Двойной удар» = реактивная аура по OnCreatureInvokedTrigger).
    [Serializable]
    public sealed class PropertyBuff : IBuffable
    {
        [SerializeReference] public ICreatureProperty Property;

        public bool Permanent => false;

        public void Apply(EcsWorld world, int source, int target)  => Property?.Apply(world, target);
        public void Revert(EcsWorld world, int source, int target) => Property?.Remove(world, target);
    }
}
