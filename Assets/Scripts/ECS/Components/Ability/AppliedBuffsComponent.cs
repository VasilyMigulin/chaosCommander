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
    }
}
