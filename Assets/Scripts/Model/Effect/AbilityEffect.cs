using Game.Core.Shared.Interface;

namespace Game.Core.Model.Effect
{
    [System.Serializable]
    public abstract class AbilityEffect : IAbilityEffect
    {
        public abstract void AddEffect(Leopotam.EcsLite.EcsWorld world, int effectEntity);
        public abstract IAbilityEffect Clone();
    }
}   
