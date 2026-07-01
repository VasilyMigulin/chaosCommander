using Game.Core.Service;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;
using System.Collections.Generic;

namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Навешивает CreatureTimerComponent на TargetEntity (TurnsRemaining = Turns).
    /// </summary>
    public struct AddCreatureTimerEffectComponent : ITargetEffect
    {
        public int Turns;
        private int abilityEntity;
        public int AbilityEntity => abilityEntity;

        public void AddComponent(EcsWorld world, Dictionary<string, int> entities)
        {
            if (entities.TryGetValue(EntityService.ABILITY_ENTITY, out int ability))
            {
                abilityEntity = ability;

                world.GetPool<AddCreatureTimerEffectComponent>().Add(ability) = new AddCreatureTimerEffectComponent()
                {
                    Turns = Turns
                };
            }
        }

        public void ApplyEffect(EcsWorld world, int effectEntity)
        {
            world.GetPool<AddCreatureTimerEffectComponent>().Add(effectEntity) = this;
        } 

        public void ApplyTarget(EcsWorld world, int targetEntity)
        {
            if (!world.GetPool<CreatureTimerComponent>().Has(targetEntity))
            {
                world.GetPool<CreatureTimerComponent>().Add(targetEntity);
            }

            ref var creatureTimerComp = ref world.GetPool<CreatureTimerComponent>().Get(targetEntity);
            creatureTimerComp.TurnsRemaining += Turns;
        }
    }
}
