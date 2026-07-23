namespace Game.Core.Ecs.Components
{
    // === struct (Component) ===
    /// <summary>Спека появления на сущности карты-существа (ставит CardCreatureModel.OnInit, если задана).
    /// Читает SpawnCreatureViewSystem при создании вью и передаёт в CreatureView.PlaySummon.
    /// Сама спека (SummonVfxSpec) лежит в Game.Core.Shared.Interface — мост Model↔Mono,
    /// Ecs.Components остаются чистыми данными.</summary>
    public struct SummonVfxComponent
    {
        public Game.Core.Shared.Interface.SummonVfxSpec Spec;
    }
}
