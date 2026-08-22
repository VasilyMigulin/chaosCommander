using System.Collections.Generic;
using Game.Core.Events;

namespace Game.Core.Ecs.Components
{
    // === struct (Component) ===
    /// <summary>
    /// Трекинг баффов, выданных РЕАКТИВНОЙ аурой — висит на сущности ИСТОЧНИКА ауры.
    /// `ApplyTrackedBuffEffect` дописывает запись на каждую новую цель (идемпотентно),
    /// `RevertTrackedBuffsEffect` снимает все по трекингу (источник ушёл/умер).
    /// Строится независимо на обоих клиентах (резолв ауры реплеится по тем же целям) → не синкается.
    /// </summary>
    public struct AppliedBuffsComponent
    {
        public List<BuffRecord> Records;
    }

    public struct BuffRecord
    {
        public int Target;
        public int Atk;
        public int Hp;
        public int Speed;
    }

    // === helper (static) ===
    /// <summary>
    /// Снятие баффов реактивной ауры (ApplyTrackedBuffEffect), выданных источником. Живёт рядом с данными
    /// (а не в Game.Core.Ability), т.к. откатывать ауру нужно и из Ecs.Systems (RunTransformSystem —
    /// полиморф источника: смерти не происходит, поэтому ApplyTrackedBuffEffect.OnSourceDied — подписка на
    /// CreatureDiedEvent — не сработает; ауру нужно откатить явно), а Ecs.Systems не видит Game.Core.Ability
    /// (см. TrackedBuffs в TrackedBuffsComponent.cs — тот же паттерн, для ДРУГОГО механизма ауры,
    /// AddBuffEffect{Tracked}; это ДВА параллельных трекинга — RunTransformSystem обязан откатывать оба).
    /// ApplyTrackedBuffEffect.RevertTrackedBuffs (Game.Core.Ability) делегирует сюда — не дублирует логику.
    /// </summary>
    public static class AppliedBuffs
    {
        public static void RevertAll(Leopotam.EcsLite.EcsWorld world, int source)
        {
            var buffsPool = world.GetPool<AppliedBuffsComponent>();
            if (!buffsPool.Has(source)) return;
            ref var buffs = ref buffsPool.Get(source);
            if (buffs.Records == null) return;

            var atkPool = world.GetPool<AttackComponent>();
            var hpPool  = world.GetPool<HealthComponent>();
            var spdPool = world.GetPool<SpeedComponent>();

            foreach (var r in buffs.Records)
            {
                if (r.Atk   != 0 && atkPool.Has(r.Target)) { ref var a = ref atkPool.Get(r.Target); a.RemoveModifier(r.Atk); }
                if (r.Hp    != 0 && hpPool.Has(r.Target))  { ref var h = ref hpPool.Get(r.Target);  h.RemoveModifier(r.Hp); GameEventBus.Publish(new CreatureHealthChangedEvent { CreatureEntity = r.Target }); }
                if (r.Speed != 0 && spdPool.Has(r.Target)) { ref var s = ref spdPool.Get(r.Target); s.RemoveModifier(r.Speed); }
            }
            buffs.Records.Clear();
        }

        /// <summary>Убрать ЦЕЛЬ из трекинга ВСЕХ источников (зовётся при смерти/уходе цели) — тот же повод и
        /// тот же баг, что чинит TrackedBuffs.RemoveTarget (см. её докстринг): КОМАНДИР возвращается в игру
        /// ТОЙ ЖЕ ecs-сущностью, и без снятия записи идемпотентность (Stack=false: «эту цель уже баффали»)
        /// навсегда блокирует повторную выдачу баффа той же реактивной аурой при повторном розыгрыше — то
        /// же самое «Королевский шарм перестаёт действовать», только для ApplyTrackedBuffEffect, у которого
        /// раньше не было своего RemoveTarget вовсе (баг 2026-08-21). В отличие от TrackedBuff, у BuffRecord
        /// нет понятия Permanent — модификатор снимается всегда.</summary>
        public static void RemoveTarget(Leopotam.EcsLite.EcsWorld world, int target)
        {
            var buffsPool = world.GetPool<AppliedBuffsComponent>();
            var atkPool = world.GetPool<AttackComponent>();
            var hpPool  = world.GetPool<HealthComponent>();
            var spdPool = world.GetPool<SpeedComponent>();

            foreach (var source in world.Filter<AppliedBuffsComponent>().End())
            {
                ref var buffs = ref buffsPool.Get(source);
                if (buffs.Records == null) continue;
                for (int i = buffs.Records.Count - 1; i >= 0; i--)
                {
                    var r = buffs.Records[i];
                    if (r.Target != target) continue;
                    if (r.Atk   != 0 && atkPool.Has(target)) { ref var a = ref atkPool.Get(target); a.RemoveModifier(r.Atk); }
                    if (r.Hp    != 0 && hpPool.Has(target))  { ref var h = ref hpPool.Get(target);  h.RemoveModifier(r.Hp); GameEventBus.Publish(new CreatureHealthChangedEvent { CreatureEntity = target }); }
                    if (r.Speed != 0 && spdPool.Has(target)) { ref var s = ref spdPool.Get(target); s.RemoveModifier(r.Speed); }
                    buffs.Records.RemoveAt(i);
                }
            }
        }
    }
}
