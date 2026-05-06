using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Обрабатывает EndTurnRequestEvent:
    ///   1. Блокирует ввод активного игрока.
    ///   2. Переводит фазу хода в TurnEndAbilities.
    ///   3. Бросает TurnEndEvent на все карты на столе.
    /// Фактический переход хода происходит в TurnEndReadySystem
    /// когда очередь способностей и эффекты завершены.
    /// </summary>
    public sealed class EndTurnRequestSystem : IEcsRunSystem
    {
        readonly EcsPoolInject<EndTurnRequestEvent> _endTurnPool = default;
        readonly EcsPoolInject<TurnPhaseState> _phasePool = default;
        readonly EcsPoolInject<TurnEndEvent> _turnEndEventPool = default;
        readonly EcsPoolInject<AbilityContainerComponent> _abilityContainerPool = default;

        readonly EcsFilterInject<Inc<EndTurnRequestEvent, TurnState, TurnPhaseState, PlayerComponent>> _requestFilter = default;
        readonly EcsFilterInject<Inc<AbilityContainerComponent, BoardTag>, Exc<HandTag, DeckTag>> _boardCardsFilter = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var entity in _requestFilter.Value)
            {
                ref var phase = ref _phasePool.Value.Get(entity);

                // Игнорируем повторные запросы если уже в фазе завершения
                if (phase.Phase != TurnPhase.PlayerTurn)
                    continue;

                phase.Phase = TurnPhase.TurnEndAbilities;
                _endTurnPool.Value.Del(entity);

                GameEventBus.Publish(new InputBlockedEvent());

                // Бросаем TurnEndEvent на все карты на столе
                foreach (var cardEntity in _boardCardsFilter.Value)
                {
                    if (!_turnEndEventPool.Value.Has(cardEntity))
                        _turnEndEventPool.Value.Add(cardEntity);
                }
            }
        }
    }
}
