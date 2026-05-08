using Leopotam.EcsLite;

namespace Game.Core.Shared.Interface
{
    public interface IAbilityPlayRequirement
    {
        bool IsSatisfied(EcsWorld world, int cardEntity);
        IAbilityPlayRequirement Clone();
    }
}
