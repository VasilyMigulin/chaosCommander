using Leopotam.EcsLite;

namespace Game.Core.Shared.Interface
{
    public interface INonTargetEffect : IAbilityEffect
    {
        void Resolve(EcsWorld world, int effectEntity);
    }
}
