using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Применяет BanishEffectComponent: убирает TargetEntity из всех зон владельца
    /// (рука/колода/доска/кладбище), уничтожает визуал и удаляет сущность из мира.
    /// </summary>
    public sealed class ApplyBanishSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsFilterInject<Inc<HitComponent, BanishEffectComponent, TargetEntityComponent>> _filter = default;
        readonly EcsPoolInject<TargetEntityComponent> _targetPool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;
        readonly EcsPoolInject<HandComponent> _handPool = default;
        readonly EcsPoolInject<DeckComponent> _deckPool = default;
        readonly EcsPoolInject<ViewRefComponent> _viewPool = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsFilterInject<Inc<PlayerComponent>> _playerFilter = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var effectEntity in _filter.Value)
            {
                int target = _targetPool.Value.Get(effectEntity).TargetEntity;
                if (target >= 0)
                    Banish(target);

                _world.Value.DelEntity(effectEntity);
            }
        }

        void Banish(int cardEntity)
        {
            int ownerId = _ownerPool.Value.Has(cardEntity) ? _ownerPool.Value.Get(cardEntity).OwnerId : -1;
            int playerEntity = FindPlayer(ownerId);

            if (playerEntity >= 0)
            {
                if (_handPool.Value.Has(playerEntity))
                {
                    ref var hand = ref _handPool.Value.Get(playerEntity);
                    if (hand.CardEntities != null && hand.CardEntities.Remove(cardEntity))
                        hand.Count = hand.CardEntities.Count;
                }
                if (_deckPool.Value.Has(playerEntity))
                {
                    ref var deck = ref _deckPool.Value.Get(playerEntity);
                    if (deck.CardEntities != null && deck.CardEntities.Remove(cardEntity))
                        deck.Count = deck.CardEntities.Count;
                }
            }

            if (_viewPool.Value.Has(cardEntity))
            {
                ref var vr = ref _viewPool.Value.Get(cardEntity);
                if (vr.View != null) UnityEngine.Object.Destroy(vr.View);
            }

            // DelEntity снимет все компоненты (BoardTag/GraveTag/HandTag/...).
            _world.Value.DelEntity(cardEntity);
        }

        int FindPlayer(int playerId)
        {
            foreach (var pe in _playerFilter.Value)
                if (_playerPool.Value.Get(pe).PlayerId == playerId) return pe;
            return -1;
        }
    }
}
