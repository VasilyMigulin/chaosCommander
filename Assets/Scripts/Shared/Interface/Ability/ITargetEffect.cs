using Leopotam.EcsLite;

namespace Game.Core.Shared.Interface
{
    public interface ITargetEffect : IAbilityEffect
    {
        void ApplyTarget(EcsWorld world, int targetEntity);
    }
}
