namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Двинуть TargetEntity (источник цепочки) на клетку (Row/Col/OwnerId).
    /// Координаты заполняются на резолве (активный) и на replay (пассивный)
    /// — НЕ читаются из ChainStateComponent на этапе apply, чтобы работать на
    /// пассивной стороне, где ChainState отсутствует.
    /// </summary>
    public struct MoveSourceToCellEffectComponent
    {
        public bool HasCell;
        public int Row;
        public int Col;
        public int OwnerId;
    }
}
