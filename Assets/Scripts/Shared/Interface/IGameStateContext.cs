using Leopotam.EcsLite;

namespace Game.Core.Shared.Interface
{
    public interface IGameStateContext
    {
        public bool IsServer { get; }
        public EcsWorld World { get; }
        void AddEntity(int entity, string localKey = null, string networkKey = null);
        bool TryGetEntity(string key, out int entity);
        string GetNetEntityKey(int entity);
    }
}
