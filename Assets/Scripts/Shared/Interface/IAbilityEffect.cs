namespace Game.Core.Shared.Interface
{
    /// <summary>
    /// Полезная нагрузка способности. Вызывается из RunResolveAbilityEffectSystem.
    /// effect entity уже содержит TargetEntityComponent с целью и владельцем.
    /// Реализация кладёт payload-компонент на ту же effect entity.
    /// Apply-системы активируются позже по HitComponent на той же entity.
    /// </summary>
    public interface IAbilityEffect
    {
        /// <param name="world">ECS-мир.</param>
        /// <param name="effectEntity">Готовая effect entity с TargetEntityComponent.
        /// Кладите payload прямо на неё.</param>
        void AddEffect(Leopotam.EcsLite.EcsWorld world, int effectEntity);
    }
}
