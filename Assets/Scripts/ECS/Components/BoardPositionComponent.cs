namespace Game.Core.Ecs.Components
{ 
    public struct BoardPositionComponent
    {
        public int Row;    // 0 = front row, 1 = back row (owner-relative)
        public int Col;    // 0-2
        public int OwnerId;
    }
}