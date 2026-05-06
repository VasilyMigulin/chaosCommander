using Leopotam.EcsLite;

namespace Game.Core.Shared.Interface
{
    public interface IAbilityCondition
    {
        void AddCondition(EcsWorld world, int abilityEntity, int cardEntity);
        bool CheckCondition(EcsWorld world, int entityAbility);
        IAbilityCondition Clone();
        void Dispose();
    }
}
