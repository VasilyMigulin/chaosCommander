using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Обрабатывает BurnEvent: перевешивает карту DeckTag → GraveTag, удаляет событие.
    /// </summary>
    public sealed class BurnCardSystem : IEcsRunSystem
    {
        readonly EcsPoolInject<BurnEvent> _burnPool = default;
        readonly EcsPoolInject<DeckTag> _deckTagPool = default;
        readonly EcsPoolInject<GraveTag> _graveTagPool = default;
        readonly EcsFilterInject<Inc<BurnEvent>> _filter = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var entity in _filter.Value)
            {
                if (_deckTagPool.Value.Has(entity))
                    _deckTagPool.Value.Del(entity);

                if (!_graveTagPool.Value.Has(entity))
                    _graveTagPool.Value.Add(entity);

                _burnPool.Value.Del(entity);
            }
        }
    }
}
