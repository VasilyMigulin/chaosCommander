using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Обрабатывает CastEvent на карте:
    ///   - снимает HandTag
    ///   - снимает карту с руки владельца
    ///   - списывает ресурс
    ///   - запускает очередь способностей (ResolvingAbilitiesState на игрока)
    ///   - для существ: вешает BoardTag + BoardPositionComponent
    ///   - для заклинаний/чар: вешает GraveTag / BoardTag соответственно
    /// </summary>
    public sealed class CastCardSystem : IEcsRunSystem
    {
        readonly EcsPoolInject<CastEvent> _castPool = default;
        readonly EcsPoolInject<HandTag> _handTagPool = default;
        readonly EcsPoolInject<BoardTag> _boardTagPool = default;
        readonly EcsPoolInject<GraveTag> _graveTagPool = default;
        readonly EcsPoolInject<CreatureTag> _creatureTagPool = default;
        readonly EcsPoolInject<SpellTag> _spellTagPool = default;
        readonly EcsPoolInject<CharmTag> _charmTagPool = default;
        readonly EcsPoolInject<BoardPositionComponent> _boardPosPool = default;
        readonly EcsPoolInject<GoldComponent> _goldPool = default;
        readonly EcsPoolInject<ManaComponent> _manaPool = default;
        readonly EcsPoolInject<GoldCostComponent> _goldCostPool = default;
        readonly EcsPoolInject<ManaCostComponent> _manaCostPool = default;
        readonly EcsPoolInject<HandComponent> _handPool = default;
        readonly EcsPoolInject<ResolvingAbilitiesState> _resolvingPool = default; 
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;
        readonly EcsPoolInject<CardModelComponent> _modelPool = default;
        readonly EcsPoolInject<RequiresTargetSelectionTag> _reqTargetPool = default;
        readonly EcsPoolInject<RequiresCardPickTag>         _reqPickPool   = default;
        readonly EcsPoolInject<RequiresRandomTargetTag>     _reqRandPool   = default;
        readonly EcsPoolInject<PlayerComponent>              _playerPool    = default;
        readonly EcsFilterInject<Inc<CastEvent>> _filter = default;
        readonly EcsPoolInject<LocalComponent>    _localPool      = default;
        readonly EcsPoolInject<CommanderTag>       _commanderPool  = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var cardEntity in _filter.Value)
            {
                // Ждём пока игрок выберет цель — TargetSelectionSystem заполнит CastEvent
                if (_reqTargetPool.Value.Has(cardEntity))
                    continue;

                // Ждём пока RandomTargetSystem найдёт случайную цель
                if (_reqRandPool.Value.Has(cardEntity))
                    continue;

                // Ждём пока игрок выберет карту из предложенных (раскопка)
                if (_reqPickPool.Value.Has(cardEntity))
                    continue;

                ref var castEvent = ref _castPool.Value.Get(cardEntity);
                int ownerEntity = castEvent.OwnerEntity;

                // --- Списываем ресурс ---
                int ownerPlayerId = _playerPool.Value.Has(ownerEntity)
                    ? _playerPool.Value.Get(ownerEntity).PlayerId
                    : ownerEntity;

                bool isLocalPlayer = _localPool.Value.Has(ownerEntity);

                if (_goldCostPool.Value.Has(cardEntity) && _goldPool.Value.Has(ownerEntity))
                {
                    ref var gold = ref _goldPool.Value.Get(ownerEntity);
                    gold.Current -= _goldCostPool.Value.Get(cardEntity).Cost;
                    GameEventBus.Publish(new ResourceChangedEvent
                    {
                        isLocalPlayer = isLocalPlayer,
                        Type = Game.Core.Service.EnumService.ResourceType.Gold,
                        NewValue = gold.Current,
                        MaxValue = gold.Max
                    });
                }
                else if (_manaCostPool.Value.Has(cardEntity) && _manaPool.Value.Has(ownerEntity))
                {
                    ref var mana = ref _manaPool.Value.Get(ownerEntity);
                    mana.Current -= _manaCostPool.Value.Get(cardEntity).Cost;
                    GameEventBus.Publish(new ResourceChangedEvent
                    {
                        isLocalPlayer = isLocalPlayer,
                        Type = Game.Core.Service.EnumService.ResourceType.Mana,
                        NewValue = mana.Current,
                        MaxValue = mana.Max
                    });
                }

                // --- Убираем с руки ---
                // Командир уходит на поле и тоже снимается с руки (HandTag + HandComponent),
                // но его персистентный UI-слот (CommanderCardView) не удаляется из ряда руки.
                bool isCommander = _commanderPool.Value.Has(cardEntity);

                if (_handTagPool.Value.Has(cardEntity))
                    _handTagPool.Value.Del(cardEntity);

                if (_handPool.Value.Has(ownerEntity))
                {
                    ref var hand = ref _handPool.Value.Get(ownerEntity);
                    RemoveFromHand(ref hand, cardEntity);
                }

                if (!isCommander && isLocalPlayer)
                    GameEventBus.Publish(new CardRemovedFromHandUIEvent { CardEntity = cardEntity });

                // --- Размещаем на доске или в могилу ---
                if (_creatureTagPool.Value.Has(cardEntity))
                {
                    _boardTagPool.Value.Add(cardEntity);
                    ref var pos = ref _boardPosPool.Value.Add(cardEntity);
                    pos.Row = castEvent.TargetCell / 5;
                    pos.Col = castEvent.TargetCell % 5;
                    pos.OwnerId = _ownerPool.Value.Has(cardEntity)
                        ? _ownerPool.Value.Get(cardEntity).OwnerId
                        : ownerEntity;
                }
                else if (_charmTagPool.Value.Has(cardEntity))
                {
                    _boardTagPool.Value.Add(cardEntity);
                }
                else if (_spellTagPool.Value.Has(cardEntity))
                {
                    _graveTagPool.Value.Add(cardEntity);
                }

                // --- Запускаем очередь способностей только если они есть ---
                var abilityContainerPool = systems.GetWorld().GetPool<AbilityContainerComponent>();
                bool hasAbilities = abilityContainerPool.Has(cardEntity)
                    && abilityContainerPool.Get(cardEntity).AbilityEntities != null
                    && abilityContainerPool.Get(cardEntity).AbilityEntities.Length > 0;

                if (hasAbilities && !_resolvingPool.Value.Has(ownerEntity))
                {
                    ref var resolving = ref _resolvingPool.Value.Add(ownerEntity);
                    resolving.CardEntity = cardEntity;
                    resolving.AbilityIndex = 0;
                }
                  
                GameEventBus.Publish(new CardPlayedEvent
                {
                    CardEntity   = cardEntity,
                    PlayerId     = ownerEntity,
                    TargetCell   = castEvent.TargetCell,
                    TargetEntity = castEvent.TargetEntity
                });

                // Уведомляем MatchTracker
                if (_modelPool.Value.Has(cardEntity))
                {
                    ref var model = ref _modelPool.Value.Get(cardEntity);
                    GameEventBus.Publish(new CardTrackedEvent
                    {
                        CardEntity = cardEntity,
                        ModelId    = model.ModelId,
                        CardName   = model.CardName,
                        PlayerId   = ownerEntity,
                        Rarity     = model.Rarity,
                        Element    = model.Element,
                        CardType   = model.CardType,
                    });
                }
            }
        }

        private static void RemoveFromHand(ref HandComponent hand, int cardEntity)
        {
            for (int i = 0; i < hand.Count; i++)
            {
                if (hand.CardEntities[i] == cardEntity)
                {
                    for (int j = i; j < hand.Count - 1; j++)
                        hand.CardEntities[j] = hand.CardEntities[j + 1];
                    hand.Count--;
                    return;
                }
            }
        }
    }
}
