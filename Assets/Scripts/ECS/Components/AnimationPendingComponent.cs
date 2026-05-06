namespace Game.Core.Ecs.Components
{
    public struct AnimationPendingComponent 
    {
        public int PendingCount; // how many animations are still in flight
    }
}