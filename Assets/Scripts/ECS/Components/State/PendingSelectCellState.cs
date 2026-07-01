namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Карта-существо ждёт от игрока клика по свободной клетке передней линии.
    /// Ставится RunRequestCreatureCardCastSystem; снимается RunSelectCellBoardSystem
    /// при получении CellClickEvent на этой же карте.
    /// </summary>
    public struct PendingSelectCellState
    {
        public int OwnerPlayerEntity;
    }
}
