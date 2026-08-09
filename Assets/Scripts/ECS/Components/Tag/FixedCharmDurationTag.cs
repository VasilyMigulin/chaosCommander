namespace Game.Core.Ecs.Components
{
    // === struct (Tag) ===
    /// <summary>
    /// Длительность этой чары ЗАФИКСИРОВАНА (CardCharmModel.FixTurns) — никакой модификатор длительности
    /// (Прокачать чары/ExtendCharmTimer, Зачарованный/CharmDurationBonusService, Зачаровать матч/
    /// MakeCharmPermanentEffect, живое превью в руке/CharmHandDurationPreviewSystem) не должен её трогать.
    /// Для карт вроде «Очарование принцессы», где длительность — часть баланса.
    /// </summary>
    public struct FixedCharmDurationTag { }
}
