using Game.Core.Service;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Добавить цвета (флаги) к TargetEntity (карте/существу): для каждого установленного
    /// бита в Colors вешается соответствующий *Tag (RedTag/BlueTag/...).
    /// </summary>
    public struct AddColorEffectComponent : ITargetEffect
    {
        private int abilityEntity;
        public int AbilityEntity { get => abilityEntity; }

        [SerializeReference] public List<IColorEffectResolver> ColorResolvers;

        public void AddComponent(EcsWorld world, Dictionary<string, int> entities)
        {
            if (entities.TryGetValue(EntityService.ABILITY_ENTITY, out int ability))
            {
                abilityEntity = ability;

                world.GetPool<AddColorEffectComponent>().Add(ability) = new AddColorEffectComponent
                {
                    ColorResolvers = new List<IColorEffectResolver>(ColorResolvers)
                };
            } 
        }

        public void ApplyEffect(EcsWorld world, int effectEntity)
        {
            world.GetPool<AddColorEffectComponent>().Add(effectEntity) = this;
        }

        public void ApplyTarget(EcsWorld world, int targetEntity)
        {
            foreach (var colorResolver in ColorResolvers)
            {
                colorResolver.Resolve(world, targetEntity, abilityEntity);
            }
        }
    }
}
