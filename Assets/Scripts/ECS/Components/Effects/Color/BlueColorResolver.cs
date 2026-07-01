using Game.Core.Shared.Interface;
using Leopotam.EcsLite;

namespace Game.Core.Ecs.Components
{
    public struct BlueColorResolver : IColorEffectResolver
    {
        public void Resolve(EcsWorld world, int targetEntity, int abilityEntity)
        {
            if (!world.GetPool<BlueTag>().Has(targetEntity))
            {
                world.GetPool<BlueTag>().Add(targetEntity);
            }
        }
    }
}