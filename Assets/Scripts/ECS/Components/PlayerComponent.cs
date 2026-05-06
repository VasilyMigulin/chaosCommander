namespace Game.Core.Ecs.Components
{
    public struct PlayerComponent 
    {
        public int PlayerId;        // уникальный id игрока (совпадает с Photon ActorNumber)
        public bool IsLocalPlayer;
    }
}