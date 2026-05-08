using Game.Core.Ecs.Components;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;
using UnityEngine;
using Game.Core.Events;
using Game.Core.Match;

namespace Game.Core.Model.Condition
{
    public class SameNameCondition : AbilityCondition
    {
        public int TargetCountCast;
        private string _trackingName;
        private EcsWorld _world;
        private int _abilityEntity;
        private int _cardEntity;

        public SameNameCondition(SameNameCondition data)
        {
            TargetCountCast = data.TargetCountCast;
        }

        public override void AddCondition(EcsWorld world, int abilityEntity, int cardEntity)
        {
            _world = world;
            _abilityEntity = abilityEntity;
            _cardEntity = cardEntity;

            ref var cardComp = ref world.GetPool<CardModelComponent>().Get(abilityEntity);
            _trackingName = cardComp.CardName;

            GameEventBus.Subscribe<CardTrackedEvent>(Track);
        } 

        void Track(CardTrackedEvent trackInfo)
        {
            if (trackInfo.CardName == _trackingName && MatchTracker.CardsPlayedWithName(_trackingName) >= TargetCountCast)
            { 
                _world.GetPool<ReadyTag>().Add(_abilityEntity);

                var abilityReadyEvent = new AbilityReadyEvent()
                {
                    AbilityEntity = _abilityEntity,
                    CardEntity = _cardEntity
                };

                GameEventBus.Publish(abilityReadyEvent);
            }
        }

        public override IAbilityCondition Clone()
        {
            return new SameNameCondition(this);
        }

        public override void Dispose()
        { 
            GameEventBus.Unsubscribe<CardTrackedEvent>(Track);
        }
    }
}