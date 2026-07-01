using Game.Core.Shared.Interface;
using Leopotam.EcsLite;

namespace Game.Core.Ecs.Components
{
    public struct YellowColorResolver : IColorEffectResolver
    {
        public void Resolve(EcsWorld world, int targetEntity, int abilityEntity)
        {
            if (!world.GetPool<YellowTag>().Has(targetEntity))
            {
                world.GetPool<YellowTag>().Add(targetEntity);
            }
        }
    }
}