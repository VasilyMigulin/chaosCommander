using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Обрабатывает BurnEvent: перевешивает карту DeckTag → GraveTag, удаляет событие.
    /// Карты с TokenTag не попадают на кладбище — entity удаляется.
    /// </summary>
    public sealed class BurnCardSystem : IEcsRunSystem
    {
        readonly EcsPoolInject<BurnEvent> _burnPool = default;
        readonly EcsPoolInject<DeckTag> _deckTagPool = default;
        readonly EcsPoolInject<HandTag> _handTagPool = default;
        readonly EcsPoolInject<GraveTag> _graveTagPool = default;
        readonly EcsPoolInject<TokenTag> _tokenTagPool = default;
        readonly EcsPoolInject<LimboTag> _limboTagPool = default;
        readonly EcsWorldInject _world = default;
        readonly EcsFilterInject<Inc<BurnEvent>> _filter = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var entity in _filter.Value)
            {
                if (_tokenTagPool.Value.Has(entity))
                {
                    // Токены не на кладбище и не удаляются — уходят в Limbo (вне геймплея, сущность жива)
                    if (_deckTagPool.Value.Has(entity)) _deckTagPool.Value.Del(entity);
                    if (_handTagPool.Value.Has(entity)) _handTagPool.Value.Del(entity);
                    if (!_limboTagPool.Value.Has(entity)) _limboTagPool.Value.Add(entity);
                    _burnPool.Value.Del(entity);
                    continue;
                }

                if (_deckTagPool.Value.Has(entity))
                    _deckTagPool.Value.Del(entity);

                if (_handTagPool.Value.Has(entity))
                    _handTagPool.Value.Del(entity);

                if (!_graveTagPool.Value.Has(entity))
                    _graveTagPool.Value.Add(entity);

                _burnPool.Value.Del(entity);
            }
        }
    }
}
