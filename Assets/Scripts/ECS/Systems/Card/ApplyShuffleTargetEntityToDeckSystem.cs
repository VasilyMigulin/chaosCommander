using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Service;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Применяет ShuffleTargetEntityEffectComponent: замешать TargetEntity
    /// (обычно — карту, выбранную раскопкой) в колоду игрока. Снимает HandTag/BoardTag/GraveTag,
    /// удаляет из соответствующих контейнеров, добавляет DeckTag и кладёт в DeckComponent.
    /// </summary>
    public sealed class ApplyShuffleTargetEntityToDeckSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsFilterInject<Inc<HitComponent, ShuffleTargetEntityEffectComponent, TargetEntityComponent, EffectAbilityRefComponent>> _filter = default;
        readonly EcsFilterInject<Inc<DeckComponent, PlayerSideComponent>> _allPlayersFilter = default;

        readonly EcsPoolInject<ShuffleTargetEntityEffectComponent> _shuffleEffPool = default;
        readonly EcsPoolInject<TargetEntityComponent> _targetPool = default;
        readonly EcsPoolInject<EffectAbilityRefComponent> _refPool = default;
        readonly EcsPoolInject<AbilitySourceComponent> _abilitySourcePool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;
        readonly EcsPoolInject<PlayerSideComponent> _sidePool = default;

        readonly EcsPoolInject<DeckComponent> _deckPool = default;
        readonly EcsPoolInject<HandComponent> _handPool = default;
        readonly EcsPoolInject<HandTag> _handTagPool = default;
        readonly EcsPoolInject<BoardTag> _boardTagPool = default;
        readonly EcsPoolInject<GraveTag> _graveTagPool = default;
        readonly EcsPoolInject<DeckTag> _deckTagPool = default;
        readonly EcsPoolInject<BoardPositionComponent> _boardPosPool = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var effectEntity in _filter.Value)
            {
                int target = _targetPool.Value.Get(effectEntity).TargetEntity;
                int abilityEntity = _refPool.Value.Get(effectEntity).AbilityEntity;
                bool intoOpp = _shuffleEffPool.Value.Get(effectEntity).IntoOpponentDeck;

                if (target >= 0)
                {
                    int casterSide = GetCasterSide(abilityEntity);
                    int destSide = intoOpp ? (casterSide == 1 ? 2 : 1) : casterSide;
                    int deckEntity = FindPlayerBySide(destSide);

                    if (deckEntity >= 0)
                        MoveToDeck(target, deckEntity);
                }

                _world.Value.DelEntity(effectEntity);
            }
        }

        int GetCasterSide(int abilityEntity)
        {
            if (!_abilitySourcePool.Value.Has(abilityEntity)) return 1;
            int sourceCard = _abilitySourcePool.Value.Get(abilityEntity).CardEntity;
            if (sourceCard < 0 || !_sidePool.Value.Has(sourceCard)) return 1;
            return _sidePool.Value.Get(sourceCard).Side;
        }

        int FindPlayerBySide(int side)
        {
            foreach (var pe in _allPlayersFilter.Value)
                if (_sidePool.Value.Get(pe).Side == side) return pe;
            return -1;
        }

        void MoveToDeck(int cardEntity, int playerEntity)
        {
            // 1) снимаем из текущей зоны
            if (_handTagPool.Value.Has(cardEntity))
                _handTagPool.Value.Del(cardEntity);
            if (_boardTagPool.Value.Has(cardEntity))
                _boardTagPool.Value.Del(cardEntity);
            if (_graveTagPool.Value.Has(cardEntity))
                _graveTagPool.Value.Del(cardEntity);
            if (_boardPosPool.Value.Has(cardEntity))
                _boardPosPool.Value.Del(cardEntity);

            // 2) убираем из руки игрока (если там лежала)
            if (_handPool.Value.Has(playerEntity))
            {
                ref var hand = ref _handPool.Value.Get(playerEntity);
                if (hand.CardEntities != null && hand.CardEntities.Remove(cardEntity))
                    hand.Count = hand.CardEntities.Count;
            }

            // 3) добавляем в колоду
            if (!_deckTagPool.Value.Has(cardEntity))
                _deckTagPool.Value.Add(cardEntity);

            ref var deck = ref _deckPool.Value.Get(playerEntity);
            if (deck.CardEntities == null)
                deck.CardEntities = new System.Collections.Generic.List<int>();

            int insertAt = deck.Count / 2;
            deck.CardEntities.Insert(insertAt, cardEntity);
            deck.Count++;
            deck.CardEntities.Shuffle();
        }
    }
}
