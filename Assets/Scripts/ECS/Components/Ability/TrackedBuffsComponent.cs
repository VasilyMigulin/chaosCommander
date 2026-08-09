using System.Collections.Generic;
using Game.Core.Shared.Interface;

namespace Game.Core.Ecs.Components
{
    // === struct (Component) ===
    /// <summary>
    /// Трекинг баффов, выданных аурой ИСТОЧНИКА (AddBuffEffect{Tracked}). При снятии (источник погиб)
    /// откатывается по списку: для каждого buff.Revert(target). Хранит IBuffable (Shared.Interface) — поэтому
    /// компонент тут, а не в Ability (Ecs.Components ссылается на Shared.Interface, не наоборот).
    /// Строится зеркально на обоих (резолв ауры реплеится по тем же целям) → не синкается.
    /// </summary>
    public struct TrackedBuffsComponent
    {
        public List<TrackedBuff> Items;
    }

    public struct TrackedBuff
    {
        public int Target;
        public IBuffable Buff;

        /// <summary>Длительность в ходах ВЛАДЕЛЬЦА источника (AddBuffEffect.Duration). 0 (умолчание) — без
        /// авто-списывания, снимается только вручную/по смерти источника, как раньше. BuffDurationTickSystem
        /// декрементит и на 0 зовёт Buff.Revert, убирая запись.</summary>
        public int TurnsRemaining;

        /// <summary>Момент тика длительности — тот же CharmTickMoment, что у чар (TurnEnd/TurnStart).</summary>
        public CharmTickMoment ExpireAt;
    }

    // === helper (static) ===
    /// <summary>
    /// Снятие баффов, выданных источником. Живёт РЯДОМ С ДАННЫМИ (а не внутри AddBuffEffect), потому что
    /// откатывать ауру должен не только сам эффект по смерти источника: источник может перестать быть
    /// собой и без гибели.
    ///
    /// Повод (2026-08-02): «Мастер трансмутаций» полиморфил «Начальника смены» — RunTransformSystem звал
    /// DisposeAbilities, эффект отписывался от CreatureDiedEvent, смерти не было, и выданный «Работяге»
    /// бафф оставался НАВСЕГДА. Сборка Ecs.Systems не видит Game.Core.Ability, поэтому позвать
    /// AddBuffEffect.RevertAll оттуда было нельзя — а компонент виден обеим.
    /// </summary>
    public static class TrackedBuffs
    {
        /// <summary>Откатить все баффы, выданные источником, и очистить трекинг. Мёртвые цели уже
        /// подчищены DieSystem → Revert по ним no-op.</summary>
        public static void RevertAll(Leopotam.EcsLite.EcsWorld world, int source)
        {
            var pool = world.GetPool<TrackedBuffsComponent>();
            if (!pool.Has(source)) return;
            ref var t = ref pool.Get(source);
            if (t.Items == null) return;
            foreach (var it in t.Items) it.Buff?.Revert(world, source, it.Target);
            t.Items.Clear();
        }
    }
}
