namespace Game.Core.Service
{
    /// <summary>
    /// Как способность получает цель. Задаётся в одном месте вместе с TargetMask + TargetShape.
    ///
    ///   Auto   — система сама находит цель по TargetMask (Self / игрок / первое существо / All).
    ///   Select — игрок интерактивно выбирает цель до розыгрыша.
    ///   Random — система выбирает случайную подходящую цель (детерминированный seed).
    ///
    /// Гейтинг по наличию валидных целей делается правилом HasMatchingEntityRule
    /// (см. Game.Core.Model.Rule), а не отдельным PlayRequirement.
    /// </summary>
    public enum TargetingMode
    {
        Auto,
        Select,
        Random,
    }
}
