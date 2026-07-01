using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using UnityEngine;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// MoveCardToGraveEvent: снимает зональные теги, убирает из руки владельца, ставит GraveTag.
    /// Токены не на кладбище — уходят в Limbo (как при сжигании/смерти). Событие снимает _delSystems.
    /// </summary>
    public sealed class RunMoveCardToGraveSystem : IEcsRunSystem
    {
        readonly EcsFilterInject<Inc<MoveCardToGraveEvent>> _filter = default;
        readonly EcsPoolInject<HandTag>   _handTagPool  = default;
        readonly EcsPoolInject<DeckTag>   _deckTagPool  = default;
        readonly EcsPoolInject<BoardTag>  _boardTagPool = default;
        readonly EcsPoolInject<GraveTag>  _graveTagPool = default;
        readonly EcsPoolInject<LimboTag>  _limboTagPool = default;
        readonly EcsPoolInject<TokenTag>  _tokenTagPool = default;
        readonly EcsPoolInject<OwnerComponent>  _ownerPool  = default;
        readonly EcsPoolInject<HandComponent>   _handPool   = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsFilterInject<Inc<PlayerComponent, HandComponent>> _playerFilter = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var card in _filter.Value)
            {
                bool wasInHand = _handTagPool.Value.Has(card);

                if (_handTagPool.Value.Has(card))  _handTagPool.Value.Del(card);
                if (_deckTagPool.Value.Has(card))  _deckTagPool.Value.Del(card);
                if (_boardTagPool.Value.Has(card)) _boardTagPool.Value.Del(card);

                RemoveFromHand(card);

                // Освобождаем UI-слот руки (заклинание уходит с руки на кладбище).
                if (wasInHand)
                    GameEventBus.Publish(new CardRemovedFromHandUIEvent { CardEntity = card });

                if (_tokenTagPool.Value.Has(card))   // токен — в Limbo, не на кладбище
                {
                    if (!_limboTagPool.Value.Has(card)) _limboTagPool.Value.Add(card);
                    continue;
                }

                if (!_graveTagPool.Value.Has(card)) _graveTagPool.Value.Add(card);
            }
        }

        void RemoveFromHand(int card)
        {
            if (!_ownerPool.Value.Has(card)) return;
            int ownerId = _ownerPool.Value.Get(card).OwnerId;
            foreach (var pe in _playerFilter.Value)
            {
                if (_playerPool.Value.Get(pe).PlayerId != ownerId) continue;
                ref var hand = ref _handPool.Value.Get(pe);
                if (hand.CardEntities != null && hand.CardEntities.Remove(card))
                    hand.Count = hand.CardEntities.Count;
                break;
            }
        }
    }
}
