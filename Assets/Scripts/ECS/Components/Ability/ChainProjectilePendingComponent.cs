namespace Game.Core.Ecs.Components
{
    // === struct (Component) ===
    /// <summary>
    /// Снаряд ТЕКУЩЕЙ стадии цепочки (RunChainSystem) в полёте — эффекты стадии ЕЩЁ НЕ применены, ждём
    /// VfxArrivedEvent. НЕ переиспользует AbilityCastPendingComponent (тот принадлежит
    /// RunResolveAbilityQueueSystem и реагирует на любой VfxArrivedEvent глобально — общий токен с
    /// ability-сущностью цепочки заставил бы обе системы дёргаться на один и тот же прилёт). Deadline —
    /// анти-софтлок: нет VfxPresenter/префаба → форсим применение по уже выбранным ChainStateComponent.LastTargets.
    /// </summary>
    public struct ChainProjectilePendingComponent
    {
        public float Deadline;   // UnityEngine.Time.time, после которого форсим стадию без прилёта
    }
}
