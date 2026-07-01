using Game.Core.Shared.Interface;

namespace Game.Core.Ecs.Components
{
    // === struct (Component) ===
    /// <summary>
    /// Контейнер эффектов на ability-сущности. RunResolveAbilityQueueSystem применяет каждый
    /// готовый (IsReady) эффект к каждой цели. Заполняется в Ability.Init.
    /// </summary>
    public struct AbilityEffectContainerComponent
    {
        public IEffect[] Effects;
    }
}
