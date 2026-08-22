namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Цепочечный аналог AbilityAnimPendingComponent — гейт анимации КАСТА на кастере для ОДНОЙ СТАДИИ
    /// цепочки (RepeatAbility/AbilityChain, ведёт RunChainSystem). Тот же принцип: VfxSpec.PlayCasterAnimation
    /// opt-in, эффекты стадии применяются на Animation Event "CastEvent" (CastApplied=false→true), гейт
    /// снимается на "FinishEvent". Отдельный компонент (не переиспользуем AbilityAnimPendingComponent) —
    /// RunResolveAbilityQueueSystem гейтит им ГЛОБАЛЬНУЮ очередь способностей, а этот — только ПРОДВИЖЕНИЕ
    /// СТАДИЙ конкретной цепочки (см. RunChainSystem.WorldSettled).
    /// Deadline — анти-софтлок: если клип не размечен обоими ивентами, форсим оба по таймауту.
    /// </summary>
    public struct ChainCastAnimPendingComponent
    {
        public float Deadline;
        public bool CastApplied;
    }
}
