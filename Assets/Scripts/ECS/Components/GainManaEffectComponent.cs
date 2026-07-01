using Game.Core.Service;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;
using System.Collections.Generic;

namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// NonTarget-эффект: добавляет ману игроку-владельцу способности на величину Amount
    /// (с клампом по Max). При Init самокопируется на ability-сущность.
    /// Реальное применение делает RunResolveGainManaEffectSystem при наличии
    /// AbilityCastEvent на той же ability.
    /// </summary>
    public struct GainManaEffectComponent : INonTargetEffect
    {
        public int Amount;
        private int abilityEntity;
        public int AbilityEntity => abilityEntity;

        public void AddComponent(EcsWorld world, Dictionary<string, int> entities)
        {
            if (entities.TryGetValue(EntityService.ABILITY_ENTITY, out int ability))
            {
                world.GetPool<GainManaEffectComponent>().Add(ability) = new GainManaEffectComponent
                {
                    abilityEntity = ability,
                    Amount = Amount,
                };
            }
        }

        public void ApplyEffect(EcsWorld world, int effectEntity)
        {
            world.GetPool<GainManaEffectComponent>().Add(effectEntity) = this;
        }

        public void Resolve(EcsWorld world, int effectEntity)
        {
            // Не используется в новом пайплайне — RunResolveGainManaEffectSystem
            // читает компонент напрямую с ability-сущности по AbilityCastEvent.
        }
    }
}
