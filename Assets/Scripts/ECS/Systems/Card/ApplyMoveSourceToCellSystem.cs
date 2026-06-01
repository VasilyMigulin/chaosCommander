using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Применяет MoveSourceToCellEffectComponent: двинуть TargetEntity на клетку,
    /// записанную в самом компоненте (Row/Col/OwnerId). Координаты заполняет
    /// резолвер на активном клиенте и реплеер на пассивном.
    /// </summary>
    public sealed class ApplyMoveSourceToCellSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsFilterInject<Inc<HitComponent, MoveSourceToCellEffectComponent, TargetEntityComponent>> _filter = default;
        readonly EcsPoolInject<TargetEntityComponent> _targetPool = default;
        readonly EcsPoolInject<MoveSourceToCellEffectComponent> _moveCellPool = default;
        readonly EcsPoolInject<MoveRequestEvent> _movePool = default;
        readonly EcsPoolInject<BoardTag> _boardPool = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var effectEntity in _filter.Value)
            {
                int targetEntity = _targetPool.Value.Get(effectEntity).TargetEntity;
                ref var data = ref _moveCellPool.Value.Get(effectEntity);

                if (targetEntity >= 0
                    && data.HasCell
                    && _boardPool.Value.Has(targetEntity)
                    && !_movePool.Value.Has(targetEntity))
                {
                    ref var req = ref _movePool.Value.Add(targetEntity);
                    req.ToRow = data.Row;
                    req.ToCol = data.Col;
                    req.ToOwnerId = data.OwnerId;
                }

                _world.Value.DelEntity(effectEntity);
            }
        }
    }
}
