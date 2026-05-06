using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using UnityEngine;
using Game.Core.Mono;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Обрабатывает MoveRequestEvent на существе:
    ///   - Обновляет BoardPositionComponent
    ///   - Двигает Transform к новой клетке
    ///   - Тратит 1 заряд SpeedComponent.Remaining
    /// </summary>
    public sealed class MoveSystem : IEcsRunSystem
    {
        readonly EcsCustomInject<BoardView> _boardView = default;
        readonly EcsFilterInject<Inc<MoveRequestEvent, CreatureTag, BoardTag, BoardPositionComponent, SpeedComponent>> _filter = default;

        readonly EcsPoolInject<MoveRequestEvent> _movePool = default;
        readonly EcsPoolInject<BoardPositionComponent> _posPool = default;
        readonly EcsPoolInject<SpeedComponent> _speedPool = default;
        readonly EcsPoolInject<TransformComponent> _transformPool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default; 

        public void Run(IEcsSystems systems)
        {
            foreach (var entity in _filter.Value)
            {
                ref var req   = ref _movePool.Value.Get(entity);
                ref var pos   = ref _posPool.Value.Get(entity);
                ref var speed = ref _speedPool.Value.Get(entity);

                if (speed.Remaining <= 0)
                {
                    _movePool.Value.Del(entity);
                    continue;
                }

                int fromRow = pos.Row;
                int fromCol = pos.Col;

                pos.Row = req.ToRow;
                pos.Col = req.ToCol;
                speed.Remaining--;

                // Двигаем GameObject к центру клетки
                if (_boardView.Value != null && _transformPool.Value.Has(entity))
                {
                    var cell = _boardView.Value.GetCell(pos.Row, pos.Col, pos.OwnerId);
                    if (cell != null)
                    {
                        ref var tr = ref _transformPool.Value.Get(entity);
                        tr.Transform.position = cell.transform.position;
                    }
                }

                ref var owner = ref _ownerPool.Value.Get(entity);
                GameEventBus.Publish(new CreatureMovedEvent
                {
                    CreatureEntity = entity,
                    FromRow = fromRow,
                    FromCol = fromCol,
                    ToRow   = pos.Row,
                    ToCol   = pos.Col,
                });

                _movePool.Value.Del(entity);
            }
        }
    }
}
