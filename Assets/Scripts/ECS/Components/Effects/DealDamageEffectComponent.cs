using Game.Core.Service;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;
using System.Collections.Generic;

namespace Game.Core.Ecs.Components
{
    public struct DealDamageEffectComponent : ITargetEffect
    {
        private int abilityEntity;

        public int BaseDamageValue;
        public int AbilityEntity => abilityEntity;

        /// <summary>
        /// Добавялем компонент на сущность способности внутри карты
        /// </summary>
        /// <param name="world"></param>
        /// <param name="entities"></param>
        public void AddComponent(EcsWorld world, Dictionary<string, int> entities)
        {
            if (entities.TryGetValue(EntityService.ABILITY_ENTITY, out int ability))
            {
                abilityEntity = ability;

                world.GetPool<DealDamageEffectComponent>().Add(ability) = new DealDamageEffectComponent
                {
                    BaseDamageValue = BaseDamageValue
                };
            }
        }
        /// <summary>
        /// Добавляем копию эффекта на временную сущность "эффекта"
        /// </summary>
        /// <param name="world"></param>
        /// <param name="effectEntity"></param>
        public void ApplyEffect(EcsWorld world, int effectEntity)
        {
            world.GetPool<DealDamageEffectComponent>().Add(effectEntity) = this;
        } 

        public void ApplyTarget(EcsWorld world, int targetEntity)
        {
            world.GetPool<TakeDamageEvent>().Add(targetEntity).Amount = BaseDamageValue;
        } 
    }
}