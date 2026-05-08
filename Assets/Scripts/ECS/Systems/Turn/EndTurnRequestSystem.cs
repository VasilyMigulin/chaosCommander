using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Photon;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Обрабатывает EndTurnRequestEvent от активного игрока.
    /// Блокирует ввод и вызывает RPC на хосте.
    /// Фазу НЕ меняет — это делает хост через RPC_TurnEndPhaseBegin.
    /// </summary>
    public sealed class EndTurnRequestSystem : IEcsRunSystem
    {
        readonly EcsPoolInject<EndTurnRequestEvent> _endTurnPool = default;
        readonly EcsPoolInject<TurnPhaseState> _phasePool = default;
        readonly EcsFilterInject<Inc<EndTurnRequestEvent, TurnState, TurnPhaseState, PlayerComponent>> _requestFilter = default;
        readonly EcsCustomInject<PhotonRunHandler> _photon = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var entity in _requestFilter.Value)
            {
                ref var phase = ref _phasePool.Value.Get(entity);

                if (phase.Phase != TurnPhase.PlayerTurn)
                    continue;

                _endTurnPool.Value.Del(entity);

                // Блокируем ввод локально
                GameEventBus.Publish(new InputBlockedEvent());

                // Просим хоста начать TurnEnd-фазу
                _photon.Value.RPC_RequestEndTurn();
            }
        }
    }
}
