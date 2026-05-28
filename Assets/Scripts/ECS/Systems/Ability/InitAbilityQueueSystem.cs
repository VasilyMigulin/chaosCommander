using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Shared.Interface;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Создаёт singleton-сущность с AbilityQueueComponent при старте.
    /// </summary>
    public sealed class InitAbilityQueueSystem : IEcsInitSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsSharedInject<IGameStateContext> _state = default;
        readonly EcsPoolInject<AbilityQueueTag> _tagPool = default;
        readonly EcsPoolInject<AbilityQueueComponent> _queuePool = default;

        public void Init(IEcsSystems systems)
        {
            var entity = _world.Value.NewEntity();
            _tagPool.Value.Add(entity);
            _queuePool.Value.Add(entity).Abilities = new();

            _state.Value.AddEntity(entity, Service.EntityService.ABILITY_QUEUE_ENTITY);
        }
    }
}
