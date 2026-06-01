using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Применяет DestroyEffectComponent: вешает DeadTag на TargetEntity (без расчёта урона).
    /// Клетка цели уже сохранена в ChainStateComponent на этапе резолва шага 0 —
    /// следующий шаг цепочки может использовать её через MoveSourceToCellEffect.
    /// </summary>
    public sealed class ApplyDestroySystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsFilterInject<Inc<HitComponent, DestroyEffectComponent, TargetEntityComponent>> _filter = default;
        readonly EcsPoolInject<TargetEntityComponent> _targetPool = default;
        readonly EcsPoolInject<DeadTag> _deadPool = default;
        readonly EcsPoolInject<BoardTag> _boardPool = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var effectEntity in _filter.Value)
            {
                int targetEntity = _targetPool.Value.Get(effectEntity).TargetEntity;

                if (targetEntity >= 0 && _boardPool.Value.Has(targetEntity) && !_deadPool.Value.Has(targetEntity))
                    _deadPool.Value.Add(targetEntity);

                _world.Value.DelEntity(effectEntity);
            }
        }
    }
}
