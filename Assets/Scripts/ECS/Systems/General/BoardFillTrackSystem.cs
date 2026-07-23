using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// Задачи: «заполнить свою сторону X раз». Каждый кадр смотрит переднюю линию (Row==0, 5 колонок) ЛОКАЛЬНОГО
    /// игрока; когда все 5 клеток призыва заняты — публикует OwnSideFilledTrackedEvent ОДИН раз на заполнение
    /// (edge-detect: было место → мест нет). Флаг живёт в экземпляре системы, который пересоздаётся на матч,
    /// поэтому между матчами сам сбрасывается. Живёт у активного клиента; трекер фильтрует по своему игроку.
    /// </summary>
    public sealed class BoardFillTrackSystem : IEcsRunSystem
    {
        readonly EcsFilterInject<Inc<PlayerComponent, LocalComponent>> _localPlayer = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;

        readonly EcsFilterInject<Inc<CreatureTag, BoardTag, BoardPositionComponent>, Exc<DeadTag>> _boardCreatures = default;
        readonly EcsPoolInject<BoardPositionComponent> _posPool = default;

        const int FrontRow = 0;   // клетки призыва — только передний ряд
        const int Cols     = 5;   // BoardNav.Cols

        bool _wasFull;

        public void Run(IEcsSystems systems)
        {
            int local = -1;
            foreach (var pe in _localPlayer.Value) { local = _playerPool.Value.Get(pe).PlayerId; break; }
            if (local < 0) return;

            int occupied = 0;
            foreach (var ce in _boardCreatures.Value)
            {
                ref var p = ref _posPool.Value.Get(ce);
                if (p.OwnerId == local && p.Row == FrontRow) occupied++;
            }

            bool full = occupied >= Cols;
            if (full && !_wasFull)
                GameEventBus.Publish(new OwnSideFilledTrackedEvent { OwnerId = local });
            _wasFull = full;
        }
    }
}
