using Game.Core.Service;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;
using System.Collections.Generic;

namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Призывает существа (или токены) указанной модели от имени владельца способности.
    /// ApplySummonSystem ищет пустые клетки чередуясь L/R от источника
    /// (если источник на доске) или от центра аватара владельца (если источник —
    /// чары/заклинание). При отсутствии места обычные карты идут в кладбище,
    /// токены — исчезают.
    /// </summary>
    public struct SummonEffectComponent : INonTargetEffect
    {
        public ISummonable TargetSummon;
        /// <summary>Фиксированное количество призывов (игнорируется при FillRow=true).</summary>
        public int Count; 
        /// <summary>Призывать пока есть свободные клетки в ряду источника/аватара.</summary>
        public bool FillRow; 
        private int abilityEntity;
        public int AbilityEntity => abilityEntity; 
        public void AddComponent(EcsWorld world, Dictionary<string, int> entities)
        {
            if (entities.TryGetValue(EntityService.ABILITY_ENTITY, out int ability))
            {
                world.GetPool<SummonEffectComponent>().Add(ability) = new SummonEffectComponent
                {
                    abilityEntity = ability,
                    Count = Count,
                    FillRow = FillRow,
                    TargetSummon = TargetSummon
                };
            }
        } 

        public void ApplyEffect(EcsWorld world, int effectEntity)
        {
            world.GetPool<SummonEffectComponent>().Add(effectEntity) = this;
        }

        public void Resolve(EcsWorld world, int effectEntity)
        {

        }
    }
}
