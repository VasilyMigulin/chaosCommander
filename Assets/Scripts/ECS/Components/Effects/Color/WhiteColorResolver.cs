using Game.Core.Shared.Interface;
using Leopotam.EcsLite;

namespace Game.Core.Ecs.Components
{
    public struct WhiteColorResolver : IColorEffectResolver
    {
        public void Resolve(EcsWorld world, int targetEntity, int abilityEntity)
        {
            if (!world.GetPool<WhiteTag>().Has(targetEntity))
            {
                world.GetPool<WhiteTag>().Add(targetEntity);
            }
        }
    }
}