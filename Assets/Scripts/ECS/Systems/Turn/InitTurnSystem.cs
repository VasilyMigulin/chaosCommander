using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Photon;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Инициализирует TurnPhaseState = WaitingForTurnStart на всех игроках.
    /// Создаёт глобальную сущность GlobalTurnState.
    /// Хост запустит первый ход через StartFirstTurn() в PhotonRunHandler после RPC_StartGame.
    /// </summary>
    public sealed class InitTurnSystem : IEcsInitSystem
    {
        public const float TurnDuration = 20f;

        readonly EcsPoolInject<TurnPhaseState> _phasePool = default;
        readonly EcsPoolInject<GlobalTurnState> _globalTurnPool = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsFilterInject<Inc<PlayerComponent>> _playersFilter = default;
        readonly EcsCustomInject<PhotonRunHandler> _photon = default;

        public void Init(IEcsSystems systems)
        {
            // Вешаем WaitingForTurnStart на каждого игрока
            foreach (var entity in _playersFilter.Value)
            {
                if (!_phasePool.Value.Has(entity))
                {
                    ref var phase = ref _phasePool.Value.Add(entity);
                    phase.Phase = TurnPhase.WaitingForTurnStart;
                }
            }

            // Создаём singleton GlobalTurnState на новой сущности
            var world = systems.GetWorld();
            var globalEntity = world.NewEntity();
            ref var global = ref _globalTurnPool.Value.Add(globalEntity);
            global.ActivePlayerId = -1;
            global.TurnNumber = 0;
            global.PersonalTurnNumber = 0;

            // Регистрируем через PhotonRunHandler (у него есть _state)
            _photon.Value.RegisterGlobalTurnEntity(globalEntity);
        }
    }
}
