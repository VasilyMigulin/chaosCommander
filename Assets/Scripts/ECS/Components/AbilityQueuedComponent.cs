namespace Game.Core.Ecs.Components
{
    public struct AbilityQueuedComponent 
    {
        public int SourceEntity;
        public int AbilityIndex;
        public int TargetEntity; // -1 if none
    }
}