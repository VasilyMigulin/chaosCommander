using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using UnityEngine;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Срабатывает однократно при переходе в фазу PlayerTurn:
    ///   - начисляет золото
    ///   - рассылает DrawCardEvent
    ///   - восстанавливает ввод
    /// </summary>
    public sealed class TurnStartResourceSystem : IEcsRunSystem
    {
        readonly EcsPoolInject<TurnState> _turnStatePool = default;
        readonly EcsPoolInject<TurnPhaseState> _phasePool = default;
        readonly EcsPoolInject<GoldComponent> _goldPool = default;
        readonly EcsPoolInject<DrawCardEvent> _drawPool = default;
        readonly EcsPoolInject<TurnResourcesGrantedTag> _grantedPool = default;
        readonly EcsFilterInject<Inc<TurnState, TurnPhaseState, GoldComponent, PlayerComponent>> _filter = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var entity in _filter.Value)
            {
                ref var phase = ref _phasePool.Value.Get(entity);
                if (phase.Phase != TurnPhase.PlayerTurn)
                    continue;

                // Однократный маркер — снимается TurnEndReadySystem при передаче хода
                if (_grantedPool.Value.Has(entity))
                    continue;

                _grantedPool.Value.Add(entity);

                ref var turnState = ref _turnStatePool.Value.Get(entity);
                ref var gold = ref _goldPool.Value.Get(entity);
                int income = GoldIncome(turnState.PersonalTurnNumber);
                gold.Current = Mathf.Min(gold.Current + income, gold.Max);

                GameEventBus.Publish(new ResourceChangedEvent
                {
                    PlayerId = entity,
                    Type = Game.Core.Service.EnumService.ResourceType.Gold,
                    NewValue = gold.Current,
                    MaxValue = gold.Max
                });

                if (!_drawPool.Value.Has(entity))
                    _drawPool.Value.Add(entity);

                // InputRestoredEvent публикует хост через RPC_PlayerTurnBegin
            }
        }

        private static int GoldIncome(int personalTurnNumber)
        {
            if (personalTurnNumber <= 3) return 1;
            if (personalTurnNumber <= 6) return 2;
            return 3;
        }
    }
}
