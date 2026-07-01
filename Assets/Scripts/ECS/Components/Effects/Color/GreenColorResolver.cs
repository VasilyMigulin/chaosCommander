using Game.Core.Shared.Interface;
using Leopotam.EcsLite;

namespace Game.Core.Ecs.Components
{
    public struct GreenColorResolver : IColorEffectResolver
    {
        public void Resolve(EcsWorld world, int targetEntity, int abilityEntity)
        {
            if (!world.GetPool<GreenTag>().Has(targetEntity))
            {
                world.GetPool<GreenTag>().Add(targetEntity);
            }
        }
    }
}