namespace Game.Core.Ecs.Components
{
    /// <summary>How many actions (move / attack) the creature may perform per turn.</summary>
    public struct SpeedComponent
    {
        public int Max;
        public int Remaining;
    } 
}