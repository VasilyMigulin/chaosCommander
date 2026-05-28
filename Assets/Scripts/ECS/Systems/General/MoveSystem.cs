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
    ///   - Обновляет BoardPositionComponent сразу (логика).
    ///   - Тратит 1 заряд SpeedComponent.Remaining.
    ///   - Запускает плавное визуальное перемещение через CreatureView.PlayMove.
    ///   - Вешает MovingTag на время анимации → блокирует ввод.
    /// </summary>
    public sealed class MoveSystem : IEcsRunSystem
    {
        readonly EcsCustomInject<BoardView> _boardView = default;
        readonly EcsFilterInject<Inc<MoveRequestEvent, CreatureTag, BoardTag, BoardPositionComponent, SpeedComponent>, Exc<MovingTag>> _filter = default;

        readonly EcsPoolInject<MoveRequestEvent>      _movePool      = default;
        readonly EcsPoolInject<BoardPositionComponent> _posPool       = default;
        readonly EcsPoolInject<SpeedComponent>         _speedPool     = default;
        readonly EcsPoolInject<TransformComponent>     _transformPool = default;
        readonly EcsPoolInject<OwnerComponent>         _ownerPool     = default;
        readonly EcsPoolInject<ViewRefComponent>       _viewPool      = default;
        readonly EcsPoolInject<MovingTag>              _movingPool    = default;

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

                int fromRow    = pos.Row;
                int fromCol    = pos.Col;
                int fromOwner  = pos.OwnerId;

                // Логическая позиция обновляется сразу
                pos.Row = req.ToRow;
                pos.Col = req.ToCol;
                if (req.ToOwnerId != 0)
                    pos.OwnerId = req.ToOwnerId;
                speed.Remaining--;
                _movePool.Value.Del(entity);

                // Целевая мировая позиция
                Vector3 targetPos = Vector3.zero;
                bool hasTarget = false;
                if (_boardView.Value != null)
                {
                    var cell = _boardView.Value.GetCell(pos.Row, pos.Col, pos.OwnerId);
                    if (cell != null)
                    {
                        targetPos = cell.transform.position;
                        hasTarget = true;
                    }
                }

                int capturedEntity = entity;
                var world          = _movingPool.Value;

                GameEventBus.Publish(new InputBlockedEvent());

                // Плавное визуальное перемещение
                CreatureView view = GetView(entity);
                if (view != null && hasTarget)
                {
                    _movingPool.Value.Add(entity);

                    view.PlayMove(targetPos, onFinished: () =>
                        {
                            if (world.Has(capturedEntity))
                                world.Del(capturedEntity);

                            GameEventBus.Publish(new InputRestoredEvent());
                        });

                        // Обновляем координаты клетки в CreatureView для корректной обработки кликов
                        view.SetCell(pos.Row, pos.Col, pos.OwnerId);
                }
                else
                {
                    // Нет вью — телепортируем мгновенно
                    if (hasTarget && _transformPool.Value.Has(entity))
                    {
                        ref var tr = ref _transformPool.Value.Get(entity);
                        tr.Transform.position = targetPos;
                    }
                    GameEventBus.Publish(new InputRestoredEvent());
                }

                GameEventBus.Publish(new CreatureMovedEvent
                {
                    CreatureEntity = entity,
                    FromRow        = fromRow,
                    FromCol        = fromCol,
                    FromOwnerId    = fromOwner,
                    ToRow          = pos.Row,
                    ToCol          = pos.Col,
                    ToOwnerId      = pos.OwnerId,
                });
            }
        }

        CreatureView GetView(int entity)
        {
            if (!_viewPool.Value.Has(entity)) return null;
            ref var vr = ref _viewPool.Value.Get(entity);
            if (vr.View == null) return null;
            return vr.View.GetComponent<CreatureView>();
        }
    }
}

