using Leopotam.EcsLite;

namespace Game.Core.Shared.Interface
{
    public interface IAbilityCondition
    {
        void AddCondition(EcsWorld world, int abilityEntity, int cardEntity);
        IAbilityCondition Clone();
        void Dispose();
    }
}
