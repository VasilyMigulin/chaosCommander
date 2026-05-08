using Game.Core.Ecs.Components;
using Game.Core.Events;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using UnityEngine;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Обрабатывает подтверждение мулигана (MulliganConfirmEvent).
    /// Когда оба игрока подтвердили — публикует AllMulligansCompletedEvent
    /// и запускает синхронизацию колод.
    /// </summary>
    public sealed class MulliganReadySystem : IEcsRunSystem
    {
        readonly EcsPoolInject<MulliganConfirmEvent> _confirmPool = default;
        readonly EcsPoolInject<MulliganComponent> _mulliganPool = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsFilterInject<Inc<MulliganConfirmEvent, MulliganComponent>> _confirmFilter = default;
        readonly EcsFilterInject<Inc<MulliganComponent, PlayerComponent>> _allMulligansFilter = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var playerEntity in _confirmFilter.Value)
            {
                ref var mulligan = ref _mulliganPool.Value.Get(playerEntity);
                mulligan.Phase = MulliganPhase.Done;

                _confirmPool.Value.Del(playerEntity);

                ref var player = ref _playerPool.Value.Get(playerEntity);
                GameEventBus.Publish(new MulliganCompletedEvent { PlayerEntity = playerEntity });
                Debug.Log($"[MulliganReadySystem] Player {player.PlayerId} confirmed mulligan");
            }

            // Проверяем все ли мулиганы завершены — только для публикации локальных UI-событий
            // AllMulligansCompletedEvent публикуется сервером через RPC_StartGame, не здесь
            bool allDone = true;
            int mulliganCount = 0;

            foreach (var e in _allMulligansFilter.Value)
            {
                mulliganCount++;
                ref var m = ref _mulliganPool.Value.Get(e);
                if (m.Phase != MulliganPhase.Done)
                {
                    allDone = false;
                    break;
                }
            }

            // Не публикуем AllMulligansCompletedEvent — за это отвечает RPC_StartGame с сервера
        }
    }
}
