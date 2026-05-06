using Game.Core.Shared.Interface;
using Game.Core.Ecs.Components;
using Leopotam.EcsLite;
using UnityEngine;

namespace Game.Core.Model.Effect
{
    public class DealDamageEffect : AbilityEffect
    {
        public int Value;

        public DealDamageEffect(DealDamageEffect data)
        {
            data.Value = Value;
        }

        public override void AddEffect(EcsWorld world, int entity)
        {
            world.GetPool<DamageComponent>().Add(entity).Value = Value;
        }

        public override IAbilityEffect Clone()
        {
            return new DealDamageEffect(this);
        }
    }
}