namespace Game.Core.Service
{
    /// <summary>
    /// Как способность получает цель. Задаётся в одном месте вместе с TargetMask + TargetShape —
    /// без отдельных PlayRequirements для выбора цели.
    ///
    ///   Auto   — система сама находит цель по TargetMask (Self / игрок / первое существо / All).
    ///   Select — игрок интерактивно выбирает цель до розыгрыша.
    ///   Random — система выбирает случайную подходящую цель (детерминированный seed).
    ///
    /// Для Select/Random Ability.Init автоматически:
    ///   • вешает RequiresTargetSelectionTag / RequiresRandomTargetTag и TargetRequirementComponent
    ///     на карту (гейтит каст до выбора цели);
    ///   • добавляет RequireValidTargetPlayRequirement (карта недоступна, если нет валидных целей).
    /// </summary>
    public enum TargetingMode
    {
        Auto,
        Select,
        Random,
    }
}
