namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Обратная ссылка с effect-entity на entity способности, которая его породила,
    /// и индекс шага цепочки. Нужна AbilityChainAdvanceSystem, чтобы понять
    /// «эффекты какого шага ещё применяются».
    /// </summary>
    public struct EffectAbilityRefComponent
    {
        public int AbilityEntity;
        public int StepIndex;
    }
}
