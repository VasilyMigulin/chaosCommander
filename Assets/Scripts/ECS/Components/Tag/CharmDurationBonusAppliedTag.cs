namespace Game.Core.Ecs.Components
{
    // === struct (Tag) ===
    /// <summary>
    /// Эта чара УЖЕ получила свой разовый бонус длительности (Зачарованный) от RunMoveCardToBoardSystem.
    /// Нужен, чтобы бонус не наматывался повторно, если чару вернули в руку (баунс) и разыграли снова —
    /// без тега второй выход на стол добавил бы CharmDurationBonusService ещё раз к той же сущности.
    /// </summary>
    public struct CharmDurationBonusAppliedTag { }
}
