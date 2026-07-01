using Leopotam.EcsLite;

namespace Game.Core.Shared.Interface
{
    /// <summary>
    /// «Бафф» — то, что можно навесить на сущность и (для аур) снять. Единая абстракция вместо россыпи
    /// эффектов: BuffStat (+ATK/HP/Speed), BuffMarker (компонент-маркер), BuffDeathTimer (таймер смерти) и т.д.
    /// Применяет/откатывает универсальный AddBuffEffect. В Shared.Interface — чтобы TrackedBuffsComponent
    /// (Ecs.Components) мог хранить IBuffable, а конкретные реализации жили в Game.Core.Ability.
    /// </summary>
    public interface IBuffable
    {
        void Apply(EcsWorld world, int source, int target);
        void Revert(EcsWorld world, int source, int target);   // для аур (снятие); one-shot — no-op
    }
}
