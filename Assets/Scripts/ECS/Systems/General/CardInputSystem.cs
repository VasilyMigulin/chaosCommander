using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Service;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Мост UI → ECS для розыгрыша карты из руки.
    ///
    /// Для карт СУЩЕСТВ:
    ///   Проверяем что хватает ресурсов → добавляем PendingTargetCardComponent(OwnFrontCell)
    ///   на entity игрока. TargetSelectionSystem подсвечивает свободные клетки фронтального
    ///   ряда и при клике создаёт CastEvent с TargetCell.
    ///   CastCardSystem разместит существо и запустит цепочку способностей.
    ///
    /// Для карт ЗАКЛИНАНИЙ:
    ///   Проверяем ресурсы + PlayableTag (условия к полю) → создаём CastEvent напрямую.
    ///   Способности запустятся через RunCastCardSystem.
    ///
    /// Выбор цели для СПОСОБНОСТЕЙ — это отдельный механизм (WaitingForTargetState),
    /// который обрабатывается уже ПОСЛЕ CastEvent в цепочке AbilityQueueSystem.
    /// </summary>
    public sealed class CardInputSystem : IEcsRunSystem, IEcsInitSystem, IEcsDestroySystem
    {
        readonly EcsWorldInject _world = default;

        readonly EcsPoolInject<PlayerComponent>   _playerPool   = default;
        readonly EcsPoolInject<TurnPhaseState>    _phasePool    = default;
        readonly EcsPoolInject<TurnState>         _turnPool     = default;

        readonly EcsPoolInject<HandTag>           _handTagPool  = default;
        readonly EcsPoolInject<OwnCardTag>        _ownCardPool  = default;
        readonly EcsPoolInject<CreatureTag>       _creaturePool = default;
        readonly EcsPoolInject<PlayableTag>       _playablePool = default;

        readonly EcsPoolInject<GoldCostComponent> _goldCostPool = default;
        readonly EcsPoolInject<ManaCostComponent> _manaCostPool = default;
        readonly EcsPoolInject<GoldComponent>     _goldPool     = default;
        readonly EcsPoolInject<ManaComponent>     _manaPool     = default;
        readonly EcsPoolInject<OwnerComponent>    _ownerPool    = default;

        readonly EcsPoolInject<PendingTargetCardComponent> _pendingPool = default;
        readonly EcsPoolInject<CastEvent>                  _castPool    = default;

        readonly EcsPoolInject<RequiresTargetSelectionTag> _reqTargetSelPool = default;
        readonly EcsPoolInject<TargetRequirementComponent> _targetReqPool    = default;

        readonly EcsPoolInject<CommanderTag>               _commanderPool   = default;
        readonly EcsPoolInject<CommanderCooldownComponent> _commanderCdPool = default;
        readonly EcsPoolInject<BoardTag>                   _boardTagPool    = default;
        readonly EcsPoolInject<CharmTag>                   _charmPool       = default;

        readonly EcsFilterInject<Inc<AttackAnimPendingTag>> _animFilter   = default;
        readonly EcsFilterInject<Inc<MovingTag>>            _movingFilter = default;

        readonly EcsFilterInject<Inc<PlayerComponent, TurnState>> _activePlayerFilter = default;
        readonly EcsFilterInject<Inc<CharmTag, BoardTag, OwnerComponent>> _boardCharmsFilter = default;

        readonly List<CardPlayRequestedEvent> _pending = new List<CardPlayRequestedEvent>();

        public void Init(IEcsSystems systems)
        {
            GameEventBus.Subscribe<CardPlayRequestedEvent>(OnCardPlayRequested);
        }
         
        public void Destroy(IEcsSystems systems)
        {
            GameEventBus.Unsubscribe<CardPlayRequestedEvent>(OnCardPlayRequested);
        } 
        private void OnCardPlayRequested(CardPlayRequestedEvent evt) => _pending.Add(evt);

        public void Run(IEcsSystems systems)
        {
            if (_pending.Count == 0) return;

            // Блокировка ввода во время анимаций
            if (_animFilter.Value.GetEntitiesCount() > 0 || _movingFilter.Value.GetEntitiesCount() > 0)
            {
                _pending.Clear();
                return;
            }

            foreach (var req in _pending)
                ProcessRequest(req);

            _pending.Clear();
        }

        private void ProcessRequest(CardPlayRequestedEvent req)
        {
            int cardEntity = req.CardEntity;
            Debug.Log($"[CardInputSystem] ProcessRequest card={cardEntity}");

            // Карта должна быть в руке локального игрока
            if (!_handTagPool.Value.Has(cardEntity)) { Debug.LogWarning($"[CardInputSystem] STOP: no HandTag on card={cardEntity}"); return; }
            if (!_ownCardPool.Value.Has(cardEntity)) { Debug.LogWarning($"[CardInputSystem] STOP: no OwnCardTag on card={cardEntity}"); return; }

            // Командир: нельзя играть пока он на поле или на кулдауне
            if (_commanderPool.Value.Has(cardEntity))
            {
                if (_boardTagPool.Value.Has(cardEntity)) { Debug.LogWarning($"[CardInputSystem] STOP: commander already on board card={cardEntity}"); return; }
                if (_commanderCdPool.Value.Has(cardEntity)) { Debug.LogWarning($"[CardInputSystem] STOP: commander on cooldown card={cardEntity}"); return; }
            }

            // Найти активного игрока в фазе PlayerTurn
            int playerEntity = FindActiveLocalPlayer();
            if (playerEntity < 0) { Debug.LogWarning($"[CardInputSystem] STOP: no active local player in PlayerTurn"); return; }

            // Ожидаем что этот игрок является владельцем карты
            if (!_ownerPool.Value.Has(cardEntity)) { Debug.LogWarning($"[CardInputSystem] STOP: no OwnerComponent on card={cardEntity}"); return; }
            ref var owner = ref _ownerPool.Value.Get(cardEntity);
            ref var player = ref _playerPool.Value.Get(playerEntity);
            if (owner.OwnerId != player.PlayerId) { Debug.LogWarning($"[CardInputSystem] STOP: owner mismatch card.owner={owner.OwnerId} player={player.PlayerId}"); return; }

            // Проверяем ресурсы
            if (!CanAfford(cardEntity, playerEntity)) { Debug.LogWarning($"[CardInputSystem] STOP: can't afford card={cardEntity}"); return; }

            // Чары: лимит под контролем одного игрока
            if (_charmPool.Value.Has(cardEntity) &&
                CountOwnedBoardCharms(player.PlayerId) >= CharmTag.MaxCharmsOnBoard)
            {
                Debug.LogWarning($"[CardInputSystem] STOP: charm limit reached for player={player.PlayerId}");
                return;
            }

            // Если уже есть pending — клик на другую карту переключает выбор
            if (_pendingPool.Value.Has(playerEntity))
            {
                ref var old = ref _pendingPool.Value.Get(playerEntity);
                // Если клик по той же карте — отменяем
                if (old.CardEntity == cardEntity)
                {
                    _pendingPool.Value.Del(playerEntity);
                    GameEventBus.Publish(new TargetSelectionCancelledEvent { CardEntity = cardEntity });
                    return;
                }
                // Переключаем на новую карту
                GameEventBus.Publish(new TargetSelectionCancelledEvent { CardEntity = old.CardEntity });
                _pendingPool.Value.Del(playerEntity);
            }

            bool isCreature = _creaturePool.Value.Has(cardEntity);
            Debug.Log($"[CardInputSystem] card={cardEntity} isCreature={isCreature}, creating cast/pending");

            if (isCreature)
            {
                // Существо: ждём выбор свободной клетки фронтального ряда
                ref var pending = ref _pendingPool.Value.Add(playerEntity);
                pending.CardEntity     = cardEntity;
                pending.RequiredTarget = TargetMask.OwnFrontCell;
                Debug.Log($"[CardInputSystem] PendingTarget added for player={playerEntity}, waiting for cell click");
            }
            else
            {
                // Заклинание / чара: проверяем PlayableTag (условия к полю)
                // Карты без AbilityPlayRequirementContainerComponent считаются всегда разыгрываемыми
                if (_playablePool.Value.Has(cardEntity) == false)
                {
                    // Есть ограничения и они не выполнены — игнорируем клик
                    var reqPool = _world.Value.GetPool<AbilityPlayRequirementContainerComponent>();
                    if (reqPool.Has(cardEntity)) { Debug.LogWarning($"[CardInputSystem] STOP: PlayableTag missing and has requirements card={cardEntity}"); return; }
                }

                if (_castPool.Value.Has(cardEntity)) { Debug.LogWarning($"[CardInputSystem] STOP: CastEvent already exists on card={cardEntity}"); return; }

                // Интерактивный выбор цели (заклинание/чара) → pending, как у существа.
                // TargetSelectionSystem подсветит валидные цели и создаст CastEvent по клику.
                if (_reqTargetSelPool.Value.Has(cardEntity) && _targetReqPool.Value.Has(cardEntity))
                {
                    ref var pendingTarget = ref _pendingPool.Value.Add(playerEntity);
                    pendingTarget.CardEntity     = cardEntity;
                    pendingTarget.RequiredTarget = _targetReqPool.Value.Get(cardEntity).RequiredTarget;
                    Debug.Log($"[CardInputSystem] spell/charm needs target selection card={cardEntity}");
                    return;
                }

                ref var castEvent = ref _castPool.Value.Add(cardEntity);
                castEvent.OwnerEntity  = playerEntity;
                castEvent.TargetEntity = -1;
                castEvent.TargetCell   = -1;
                Debug.Log($"[CardInputSystem] CastEvent created for spell/charm card={cardEntity}");
            }
        }

        private bool CanAfford(int cardEntity, int playerEntity)
        {
            if (_goldCostPool.Value.Has(cardEntity))
            {
                if (!_goldPool.Value.Has(playerEntity)) return false;
                return _goldPool.Value.Get(playerEntity).Current >= _goldCostPool.Value.Get(cardEntity).Cost;
            }
            if (_manaCostPool.Value.Has(cardEntity))
            {
                if (!_manaPool.Value.Has(playerEntity)) return false;
                return _manaPool.Value.Get(playerEntity).Current >= _manaCostPool.Value.Get(cardEntity).Cost;
            }
            return true;
        }

        private int CountOwnedBoardCharms(int playerId)
        {
            int n = 0;
            foreach (var ce in _boardCharmsFilter.Value)
                if (_ownerPool.Value.Get(ce).OwnerId == playerId) n++;
            return n;
        }

        private int FindActiveLocalPlayer()
        {
            foreach (var pe in _activePlayerFilter.Value)
            {
                if (!_playerPool.Value.Get(pe).IsLocalPlayer) continue;
                if (!_phasePool.Value.Has(pe)) continue;
                if (_phasePool.Value.Get(pe).Phase != TurnPhase.PlayerTurn) continue;
                return pe;
            }
            return -1;
        }
    }
}
