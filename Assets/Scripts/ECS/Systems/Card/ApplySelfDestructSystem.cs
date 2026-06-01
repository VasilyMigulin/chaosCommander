using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Применяет SelfDestructEffectComponent: вешает DeadTag на карту-источник
    /// способности (если та на доске). Используется для «При разыгрывании: умирает».
    /// </summary>
    public sealed class ApplySelfDestructSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsFilterInject<Inc<HitComponent, SelfDestructEffectComponent, EffectAbilityRefComponent>> _filter = default;
        readonly EcsPoolInject<EffectAbilityRefComponent> _refPool = default;
        readonly EcsPoolInject<AbilitySourceComponent> _abilitySourcePool = default;
        readonly EcsPoolInject<BoardTag> _boardPool = default;
        readonly EcsPoolInject<DeadTag> _deadPool = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var effectEntity in _filter.Value)
            {
                int abilityEntity = _refPool.Value.Get(effectEntity).AbilityEntity;
                if (_abilitySourcePool.Value.Has(abilityEntity))
                {
                    int sourceCard = _abilitySourcePool.Value.Get(abilityEntity).CardEntity;
                    if (sourceCard >= 0
                        && _boardPool.Value.Has(sourceCard)
                        && !_deadPool.Value.Has(sourceCard))
                    {
                        _deadPool.Value.Add(sourceCard);
                    }
                }

                _world.Value.DelEntity(effectEntity);
            }
        }
    }
}
