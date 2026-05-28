using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Network;
using Game.Core.Photon;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using MemoryPack;
using System.Collections.Generic;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Собирает входные действия локального (активного) игрока в типизированные IActionData
    /// и немедленно отправляет каждое оппоненту через PhotonRunHandler.
    ///
    /// Записываются только входные действия (что и как запускаем).
    /// Результаты (урон, смерть, добор и т.д.) воспроизводятся детерминированно на обеих сторонах.
    ///
    /// ActionIndex нумеруется в рамках одного хода и сбрасывается при TurnStartedEvent.
    /// </summary>
    public sealed class CollectActionSystem : IEcsInitSystem, IEcsRunSystem, System.IDisposable
    {
        readonly EcsWorldInject _world = default;
        readonly EcsCustomInject<PhotonRunHandler> _photon = default;
        readonly EcsPoolInject<NetworkEntityComponent> _netKeyPool = default;
        readonly EcsPoolInject<OwnCardTag> _ownCardPool = default;

        private int _currentTurnNumber;
        private int _actionIndex;

        private readonly List<IActionData> _pendingActions = new List<IActionData>();

        // ── Init / Destroy ────────────────────────────────────────────────────

        public void Init(IEcsSystems systems)
        {
            GameEventBus.Subscribe<TurnStartedEvent>(OnTurnStarted);
            GameEventBus.Subscribe<CardPlayedEvent>(OnCardPlayed);
            GameEventBus.Subscribe<CreatureMovedEvent>(OnCreatureMoved);
            GameEventBus.Subscribe<CreatureAttackedEvent>(OnCreatureAttacked);
            GameEventBus.Subscribe<CardPickResolvedNetEvent>(OnCardPicked);
            // TODO: подписаться на AbilityActivatedEvent когда будут активные способности
        }

        public void Dispose()
        {
            GameEventBus.Unsubscribe<TurnStartedEvent>(OnTurnStarted);
            GameEventBus.Unsubscribe<CardPlayedEvent>(OnCardPlayed);
            GameEventBus.Unsubscribe<CreatureMovedEvent>(OnCreatureMoved);
            GameEventBus.Unsubscribe<CreatureAttackedEvent>(OnCreatureAttacked);
            GameEventBus.Unsubscribe<CardPickResolvedNetEvent>(OnCardPicked);
        }

        // ── ECS Run ───────────────────────────────────────────────────────────

        public void Run(IEcsSystems systems)
        {
            if (_pendingActions.Count == 0) return;
            if (_photon.Value == null) { _pendingActions.Clear(); return; }

            foreach (var action in _pendingActions)
                SendAction(action);

            _pendingActions.Clear();
        }

        // ── Event handlers ────────────────────────────────────────────────────

        private void OnTurnStarted(TurnStartedEvent evt)
        {
            _currentTurnNumber = evt.TurnNumber;
            _actionIndex = 0;
        }

        private void OnCardPlayed(CardPlayedEvent evt)
        {
            if (!IsOwnCard(evt.CardEntity)) return;

            Enqueue(new ActionCastData
            {
                TurnNumber      = _currentTurnNumber,
                ActionIndex     = _actionIndex++,
                SourceEntityKey = GetNetKey(evt.CardEntity),
                TargetEntityKey = GetNetKey(evt.TargetEntity),
                TargetCell      = evt.TargetCell,
            });
        }

        private void OnCreatureMoved(CreatureMovedEvent evt)
        {
            if (!IsOwnCard(evt.CreatureEntity)) return;

            Enqueue(new ActionMoveData
            {
                TurnNumber      = _currentTurnNumber,
                ActionIndex     = _actionIndex++,
                SourceEntityKey = GetNetKey(evt.CreatureEntity),
                TargetCell      = evt.ToRow * 5 + evt.ToCol,
            });
        }

        private void OnCreatureAttacked(CreatureAttackedEvent evt)
        {
            if (!IsOwnCard(evt.AttackerEntity)) return;

            Enqueue(new ActionAttackData
            {
                TurnNumber          = _currentTurnNumber,
                ActionIndex         = _actionIndex++,
                AttackerEntityKey   = GetNetKey(evt.AttackerEntity),
                DefenderEntityKey   = GetNetKey(evt.DefenderEntity),
            });
        }

        private void OnCardPicked(CardPickResolvedNetEvent evt)
        {
            if (!IsOwnCard(evt.CastingCardEntity)) return;

            Enqueue(new ActionCardPickedData
            {
                TurnNumber        = _currentTurnNumber,
                ActionIndex       = _actionIndex++,
                SourceEntityKey   = !string.IsNullOrEmpty(evt.CastingCardNetworkKey)
                    ? evt.CastingCardNetworkKey
                    : GetNetKey(evt.CastingCardEntity),
                ChosenEntityKey   = evt.ChosenCardNetworkKey,
                CreateFromPool    = evt.CreateFromPool,
                ChosenExpansionId = evt.ChosenExpansionId,
                ChosenCardId      = evt.ChosenCardId,
            });
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void Enqueue(IActionData action)
        {
            _pendingActions.Add(action);
        }

        private void SendAction(IActionData action)
        {
            byte[] data = MemoryPackSerializer.Serialize(action);
            _photon.Value.RPC_SendActionSnapshot(data);
        }

        private bool IsOwnCard(int entity)
        {
            return entity >= 0 && _ownCardPool.Value.Has(entity);
        }

        private string GetNetKey(int entity)
        {
            if (entity < 0) return "";
            if (_netKeyPool.Value.Has(entity))
                return _netKeyPool.Value.Get(entity).NetworkEntityKey;
            return "";
        }
    }
}
