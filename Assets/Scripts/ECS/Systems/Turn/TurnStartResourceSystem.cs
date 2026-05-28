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
        readonly EcsPoolInject<ManaComponent> _manaPool = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsPoolInject<DrawCardEvent> _drawPool = default;
        readonly EcsPoolInject<TurnResourcesGrantedTag> _grantedPool = default;
        readonly EcsFilterInject<Inc<TurnState, TurnPhaseState, GoldComponent, ManaComponent, PlayerComponent>> _filter = default;
        readonly EcsPoolInject<LocalComponent> _localPool = default;

        // Восстановление скорости существ владельца в начале хода
        readonly EcsFilterInject<Inc<CreatureTag, BoardTag, SpeedComponent, OwnerComponent>, Exc<DeadTag>> _creaturesFilter = default;
        readonly EcsPoolInject<SpeedComponent> _speedPool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;

        // Кулдаун командира владельца
        readonly EcsFilterInject<Inc<CommanderTag, CommanderCooldownComponent, OwnerComponent>> _commanderCdFilter = default;
        readonly EcsPoolInject<CommanderCooldownComponent> _commanderCdPool = default;

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

                ref var player = ref _playerPool.Value.Get(entity);
                RestoreCreatureSpeed(player.PlayerId);
                TickCommanderCooldown(player.PlayerId);

                ref var turnState = ref _turnStatePool.Value.Get(entity);
                ref var goldComp = ref _goldPool.Value.Get(entity);
                ref var manaComp = ref _manaPool.Value.Get(entity);

                int income = GoldIncome(turnState.PersonalTurnNumber);
                goldComp.Max = Mathf.Min(goldComp.Max + income, 10);
                goldComp.Current = goldComp.Max;

                manaComp.Max = goldComp.Max; 

                bool isLocalPlayer = _localPool.Value.Has(entity);

                GameEventBus.Publish(new ResourceChangedEvent
                {
                    isLocalPlayer = isLocalPlayer,
                    Type = Service.EnumService.ResourceType.Gold,
                    NewValue = goldComp.Current,
                    MaxValue = goldComp.Max
                });

                GameEventBus.Publish(new ResourceChangedEvent
                {
                    isLocalPlayer = isLocalPlayer,
                    Type = Service.EnumService.ResourceType.Mana,
                    NewValue = manaComp.Current,
                    MaxValue = manaComp.Max
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

        private void RestoreCreatureSpeed(int playerId)
        {
            foreach (var ce in _creaturesFilter.Value)
            {
                ref var owner = ref _ownerPool.Value.Get(ce);
                if (owner.OwnerId != playerId) continue;

                ref var speed = ref _speedPool.Value.Get(ce);
                speed.Remaining = speed.Max;
            }
        }

        // Кулдаун командира тикает в начале хода владельца.
        // TurnsRemaining=1 (после гибели) → командир заблокирован на этом ходу,
        // освобождается в начале следующего своего хода (пропуск одного хода).
        private void TickCommanderCooldown(int playerId)
        {
            foreach (var ce in _commanderCdFilter.Value)
            {
                ref var owner = ref _ownerPool.Value.Get(ce);
                if (owner.OwnerId != playerId) continue;

                ref var cd = ref _commanderCdPool.Value.Get(ce);
                if (cd.TurnsRemaining > 0)
                {
                    cd.TurnsRemaining--;
                }
                else
                {
                    _commanderCdPool.Value.Del(ce);
                    GameEventBus.Publish(new CommanderCooldownExpiredUIEvent { CardEntity = ce });
                }
            }
        }
    }
}
