namespace Game.Core.Service
{
    /// <summary>
    /// Откуда шаг N (N > 0) берёт цель для своих эффектов.
    /// Шаг 0 (исходные Effects абилки) всегда использует таргет/маску/форму абилки —
    /// этот enum применяется только к ChainSteps[1..].
    /// </summary>
    public enum ChainTargetSource
    {
        /// <summary>Цель = та же, что у предыдущего шага.</summary>
        PreviousTarget,

        /// <summary>Цель = entity-результат предыдущего шага (например, выбранная при раскопке карта).</summary>
        PreviousProduced,

        /// <summary>Цель = кастер (карта-источник способности).</summary>
        Source,

        /// <summary>
        /// Цель = клетка, захваченная предыдущим шагом (для эффектов перемещения).
        /// Сам TargetEntity при этом ставится в источник: «двигай меня на сохранённую клетку».
        /// </summary>
        PreviousCell,

        /// <summary>
        /// Цель = карта, выбранная раскопкой при касте (RequireCardPickPlayRequirement).
        /// Берётся из ChainState.PickedCardEntity, сохранённого ChainAdvanceSystem при входе.
        /// </summary>
        PickedCard,
    }
}
