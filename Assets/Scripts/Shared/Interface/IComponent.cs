using System.Collections.Generic;

namespace Game.Core.Shared.Interface
{
    public interface IComponent
    {
        void AddComponent(Leopotam.EcsLite.EcsWorld world, Dictionary<string, int> entities);
    }
}
