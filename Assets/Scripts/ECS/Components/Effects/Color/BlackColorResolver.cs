using Game.Core.Shared.Interface;
using Leopotam.EcsLite;

namespace Game.Core.Ecs.Components
{
    public struct BlackColorResolver : IColorEffectResolver
    { 
        public void Resolve(EcsWorld world, int targetEntity, int abilityEntity)
        {
            if (!world.GetPool<BlackTag>().Has(targetEntity))
            {
                world.GetPool<BlackTag>().Add(targetEntity);
            }
        }
    }
}