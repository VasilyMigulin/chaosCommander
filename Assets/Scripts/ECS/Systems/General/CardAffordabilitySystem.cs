using Game.Core.Ecs.Components;
using Game.Core.Events;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Следит за доступностью карт в руке локального игрока для розыгрыша:
    /// при изменении ресурсов (ResourceChangedEvent) пересчитывает все карты
    /// в руке и публикует CardAffordableChangedEvent для каждой.
    /// </summary>
    public sealed class CardAffordabilitySystem : IEcsInitSystem, IEcsRunSystem, IEcsDestroySystem
    {
        readonly EcsWorldInject _world = default;

        readonly EcsPoolInject<HandTag>          _handTagPool   = default;
        readonly EcsPoolInject<OwnCardTag>        _ownCardPool   = default;
        readonly EcsPoolInject<GoldCostComponent> _goldCostPool  = default;
        readonly EcsPoolInject<ManaCostComponent> _manaCostPool  = default;
        readonly EcsPoolInject<HealthCostComponent> _healthCostPool = default;
        readonly EcsPoolInject<OwnerComponent>    _ownerPool     = default;
        readonly EcsPoolInject<GoldComponent>     _goldPool      = default;
        readonly EcsPoolInject<ManaComponent>     _manaPool      = default;
        readonly EcsPoolInject<PlayerComponent>   _playerPool    = default;
        readonly EcsPoolInject<ActiveState>       _activePool    = default;

        readonly EcsFilterInject<Inc<HandTag, OwnCardTag>> _handFilter = default;

        bool _resourceDirty;

        public void Init(IEcsSystems systems)
        {
            GameEventBus.Subscribe<ResourceChangedEvent>(OnResourceChanged);
            GameEventBus.Subscribe<CardDrawnEvent>(OnCardDrawn);
            GameEventBus.Subscribe<CardPlacedInHandViewEvent>(OnCardPlacedInView);
            GameEventBus.Subscribe<AbilityReadyEvent>(OnAbilityReady);
            GameEventBus.Subscribe<AbilityNotReadyEvent>(OnAbilityNotReady);
            // Доступность завязана на ход (ActiveState) → пересчитываем на границах хода:
            // когда локальный стал активным и когда ход ушёл оппоненту.
            GameEventBus.Subscribe<LocalTurnStartedEvent>(OnLocalTurnStarted);
            GameEventBus.Subscribe<OpponentTurnEndedEvent>(OnOpponentTurnEnded);
            GameEventBus.Subscribe<CostModifierChangedEvent>(OnCostModifierChanged);
        }

        private void OnResourceChanged(ResourceChangedEvent _)     => _resourceDirty = true;
        private void OnCardDrawn(CardDrawnEvent _)                  => _resourceDirty = true;
        private void OnLocalTurnStarted(LocalTurnStartedEvent _)    => _resourceDirty = true;
        private void OnOpponentTurnEnded(OpponentTurnEndedEvent _)  => _resourceDirty = true;
        private void OnCostModifierChanged(CostModifierChangedEvent _) => _resourceDirty = true;

        private void OnCardPlacedInView(CardPlacedInHandViewEvent evt)
        {
            int cardEntity = evt.CardEntity;
            if (!_handTagPool.Value.Has(cardEntity)) return;
            if (!_ownCardPool.Value.Has(cardEntity)) return;
            GameEventBus.Publish(new CardAffordableChangedEvent
            {
                CardEntity   = cardEntity,
                IsAffordable = IsAffordable(cardEntity),
            });
        }

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

                if (TryEffectiveCost(cardEntity, out int eff))
                    GameEventBus.Publish(new CardCostChangedEvent { CardEntity = cardEntity, EffectiveCost = eff });
            }
        }

        // Эффективная стоимость карты (база + модификатор владельца). false — у карты нет стоимости.
        private bool TryEffectiveCost(int cardEntity, out int effective)
        {
            effective = 0;
            if (!_ownerPool.Value.Has(cardEntity)) return false;
            int ownerEntity = FindPlayerEntity(_ownerPool.Value.Get(cardEntity).OwnerId);
            if (ownerEntity < 0) return false;

            if (_goldCostPool.Value.Has(cardEntity))
            { effective = CostModifierUtil.Effective(_world.Value, ownerEntity, _goldCostPool.Value.Get(cardEntity).Cost); return true; }
            if (_manaCostPool.Value.Has(cardEntity))
            { effective = CostModifierUtil.Effective(_world.Value, ownerEntity, _manaCostPool.Value.Get(cardEntity).Cost); return true; }
            if (_healthCostPool.Value.Has(cardEntity))
            { effective = CostModifierUtil.Effective(_world.Value, ownerEntity, _healthCostPool.Value.Get(cardEntity).Cost); return true; }
            return false;
        }

        private bool IsAffordable(int cardEntity)
        {
            if (!_ownerPool.Value.Has(cardEntity)) return false;
            int ownerEntity = FindPlayerEntity(_ownerPool.Value.Get(cardEntity).OwnerId);
            if (ownerEntity < 0) return false;

            // Карта доступна к розыгрышу ТОЛЬКО в свой ход. ActiveState висит на активном игроке;
            // на чужом ходу его нет → карты не подсвечиваются и не перетаскиваются (см. также гейт
            // в RunCastRouterSystem — defense-in-depth).
            if (!_activePool.Value.Has(ownerEntity)) return false;

            if (_goldCostPool.Value.Has(cardEntity) && _goldPool.Value.Has(ownerEntity))
            {
                ref var gold = ref _goldPool.Value.Get(ownerEntity);
                return gold.Current >= CostModifierUtil.Effective(_world.Value, ownerEntity, _goldCostPool.Value.Get(cardEntity).Cost);
            }

            if (_manaCostPool.Value.Has(cardEntity) && _manaPool.Value.Has(ownerEntity))
            {
                ref var mana = ref _manaPool.Value.Get(ownerEntity);
                return mana.Current >= CostModifierUtil.Effective(_world.Value, ownerEntity, _manaCostPool.Value.Get(cardEntity).Cost);
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

        public void Destroy(IEcsSystems systems)
        {
            GameEventBus.Unsubscribe<ResourceChangedEvent>(OnResourceChanged);
            GameEventBus.Unsubscribe<CardDrawnEvent>(OnCardDrawn);
            GameEventBus.Unsubscribe<CardPlacedInHandViewEvent>(OnCardPlacedInView);
            GameEventBus.Unsubscribe<AbilityReadyEvent>(OnAbilityReady);
            GameEventBus.Unsubscribe<AbilityNotReadyEvent>(OnAbilityNotReady);
            GameEventBus.Unsubscribe<LocalTurnStartedEvent>(OnLocalTurnStarted);
            GameEventBus.Unsubscribe<OpponentTurnEndedEvent>(OnOpponentTurnEnded);
            GameEventBus.Unsubscribe<CostModifierChangedEvent>(OnCostModifierChanged);
        }
    }
}
