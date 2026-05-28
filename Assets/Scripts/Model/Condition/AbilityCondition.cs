using Game.Core.Shared.Interface;
using Leopotam.EcsLite;

namespace Game.Core.Model.Condition
{
    [System.Serializable]
    public abstract class AbilityCondition : IAbilityCondition
    {
        public abstract void AddCondition(EcsWorld world, int abilityEntity, int cardEntity);
        public abstract IAbilityCondition Clone(); 
        public abstract void Dispose();
    }
}
