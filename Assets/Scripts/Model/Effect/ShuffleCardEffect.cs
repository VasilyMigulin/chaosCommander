using Game.Core.Shared.Interface;
using Game.Core.Ecs.Components; 
using Leopotam.EcsLite;
using System.Collections.Generic;
using Game.Core.Shared;

namespace Game.Core.Model.Effect
{ 
    /// <summary>
    /// Замешивает карту CardModel в колоду целевого игрока.
    /// Если CardModel.IsToken — при вставке добавляется TokenTag.
    /// Цель задаётся через AbilityChosenTargetComponent или таргет-флаги (AllyPlayer / EnemyPlayer).
    /// </summary>
    public class ShuffleCardEffect : AbilityEffect
    {
        /// <summary>Карты для создания entity.</summary>
        public List<ShuffleCardData> CardsToShuffle;
        /// <summary>
        /// Если true — карта замешивается в колоду врага, иначе союзного игрока.
        /// Переопределяется флагами targeting, но хранится как удобный дефолт.
        /// </summary>
        public bool IntoOpponentDeck;

        public ShuffleCardEffect() 
        {
            CardsToShuffle = new List<ShuffleCardData>();
            IntoOpponentDeck = false;
        }

        public ShuffleCardEffect(List<ShuffleCardData> cards, bool intoOpponentDeck = false)
        {
            CardsToShuffle = cards;
            IntoOpponentDeck = intoOpponentDeck;
        }

        private ShuffleCardEffect(ShuffleCardEffect source)
        {
            CardsToShuffle = new List<ShuffleCardData>(source.CardsToShuffle);
            IntoOpponentDeck = source.IntoOpponentDeck;
        }

        public override void AddEffect(EcsWorld world, int effectEntity)
        {
            // effect entity уже содержит TargetEntityComponent с OwnerEntity. 
            ref var comp = ref world.GetPool<ShuffleCardEffectComponent>().Add(effectEntity);
            comp.Cards = new List<ShuffleCardData>(CardsToShuffle);
        }

        public override IAbilityEffect Clone() => new ShuffleCardEffect(this);
    }
}
