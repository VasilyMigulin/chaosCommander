namespace Game.Core.Ecs.Components
{
    // === struct (Component) ===
    /// <summary>
    /// Обратная ссылка ability-сущности на владельцев: карта-источник, игрок-владелец и индекс
    /// способности в карте (нужен резолв-системе как кастер и для снапшота/синка). Ставится в Ability.Init.
    /// </summary>
    public struct AbilityOwnerComponent
    {
        public int CardEntity;
        public int PlayerEntity;
        public int AbilityIndex;
        /// <summary>Индекс шага цепочки (0 для обычных способностей). Для синка многошаговых
        /// способностей: пассив адресует стадию по (AbilityIndex, StepIndex). См. ActionAbilityData.</summary>
        public int StepIndex;
    }
}
