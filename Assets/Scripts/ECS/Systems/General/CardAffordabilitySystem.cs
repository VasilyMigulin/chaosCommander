using Game.Core.Ecs.Components;
using Game.Core.Events;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Следит за доступностью карт в руке локального игрока для розыгрыша:
    ///
    ///   1. При изменении ресурсов (ResourceChangedEvent) — пересчитывает все карты
    ///      в руке и публикует CardAffordableChangedEvent для каждой.
    ///
    ///   2. При появлении/снятии ReadyTag на способности (AbilityReadyEvent /
    ///      AbilityNotReadyEvent) — публикует CardAbilityReadyChangedEvent для
    ///      карты-владельца способности.
    /// </summary>
    public sealed class CardAffordabilitySystem : IEcsRunSystem, System.IDisposable
    {
        readonly EcsWorldInject _world = default;

        readonly EcsPoolInject<HandTag>          _handTagPool   = default;
        readonly EcsPoolInject<OwnCardTag>        _ownCardPool   = default;
        readonly EcsPoolInject<GoldCostComponent> _goldCostPool  = default;
        readonly EcsPoolInject<ManaCostComponent> _manaCostPool  = default;
        readonly EcsPoolInject<OwnerComponent>    _ownerPool     = default;
        readonly EcsPoolInject<GoldComponent>     _goldPool      = default;
        readonly EcsPoolInject<ManaComponent>     _manaPool      = default;
        readonly EcsPoolInject<PlayerComponent>   _playerPool    = default;

        readonly EcsFilterInject<Inc<HandTag, OwnCardTag>> _handFilter = default;

        bool _resourceDirty;

        public void Init(IEcsSystems systems)
        {
            GameEventBus.Subscribe<ResourceChangedEvent>(OnResourceChanged);
            GameEventBus.Subscribe<AbilityReadyEvent>(OnAbilityReady);
            GameEventBus.Subscribe<AbilityNotReadyEvent>(OnAbilityNotReady);
        }

        private void OnResourceChanged(ResourceChangedEvent _) => _resourceDirty = true;

        private void OnAbilityReady(AbilityReadyEvent evt)
        {
            if (evt.CardEntity < 0) return;
            GameEventBus.Publish(new CardAbilityReadyChangedEvent
            {
                CardEntity = evt.CardEntity,
                IsReady    = true,
            });
        }

        private void OnAbilityNotReady(AbilityNotReadyEvent evt)
        {
            if (evt.CardEntity < 0) return;
            GameEventBus.Publish(new CardAbilityReadyChangedEvent
            {
                CardEntity = evt.CardEntity,
                IsReady    = false,
            });
        }

        public void Run(IEcsSystems systems)
        {
            if (!_resourceDirty) return;
            _resourceDirty = false;

            foreach (var cardEntity in _handFilter.Value)
            {
                bool affordable = IsAffordable(cardEntity);
                GameEventBus.Publish(new CardAffordableChangedEvent
                {
                    CardEntity   = cardEntity,
                    IsAffordable = affordable,
                });
            }
        }

        private bool IsAffordable(int cardEntity)
        {
            if (!_ownerPool.Value.Has(cardEntity)) return false;
            int ownerEntity = FindPlayerEntity(_ownerPool.Value.Get(cardEntity).OwnerId);
            if (ownerEntity < 0) return false;

            if (_goldCostPool.Value.Has(cardEntity) && _goldPool.Value.Has(ownerEntity))
            {
                ref var gold = ref _goldPool.Value.Get(ownerEntity);
                return gold.Current >= _goldCostPool.Value.Get(cardEntity).Cost;
            }

            if (_manaCostPool.Value.Has(cardEntity) && _manaPool.Value.Has(ownerEntity))
            {
                ref var mana = ref _manaPool.Value.Get(ownerEntity);
                return mana.Current >= _manaCostPool.Value.Get(cardEntity).Cost;
            }

            // Карта без стоимости — всегда доступна
            return true;
        }

        private int FindPlayerEntity(int playerId)
        {
            var filter = _world.Value.Filter<PlayerComponent>().End();
            foreach (var e in filter)
            {
                if (_playerPool.Value.Get(e).PlayerId == playerId)
                    return e;
            }
            return -1;
        }

        public void Dispose()
        {
            GameEventBus.Unsubscribe<ResourceChangedEvent>(OnResourceChanged);
            GameEventBus.Unsubscribe<AbilityReadyEvent>(OnAbilityReady);
            GameEventBus.Unsubscribe<AbilityNotReadyEvent>(OnAbilityNotReady);
        }
    }
}
