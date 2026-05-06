using Game.Core.Shared.Interface;
using UnityEngine;

namespace Game.Core.Model.Effect
{
    public abstract class AbilityEffect : IAbilityEffect
    {
        public abstract void AddEffect(Leopotam.EcsLite.EcsWorld world, int entity);
        public abstract IAbilityEffect Clone();
    }
}   