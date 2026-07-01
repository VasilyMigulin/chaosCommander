using Game.Core.Shared.Interface;
using Leopotam.EcsLite;

namespace Game.Core.Ecs.Components
{
    public struct RedColorResolver : IColorEffectResolver
    {
        public void Resolve(EcsWorld world, int targetEntity, int abilityEntity)
        {
            if (!world.GetPool<RedTag>().Has(targetEntity))
            {
                world.GetPool<RedTag>().Add(targetEntity);
            }
        }
    }
}