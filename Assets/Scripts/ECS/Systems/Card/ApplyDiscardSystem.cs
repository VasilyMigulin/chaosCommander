using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Discard: сбрасывает N карт из руки целевого игрока.
    /// Порядок: с конца руки (последняя добранная) — детерминировано на обоих клиентах.
    /// Карты с TokenTag исчезают, остальные идут на кладбище.
    /// </summary>
    public sealed class ApplyDiscardSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsFilterInject<Inc<HitComponent, DiscardEffectComponent, TargetEntityComponent>> _filter = default;
        readonly EcsPoolInject<DiscardEffectComponent> _discardPool = default;
        readonly EcsPoolInject<TargetEntityComponent> _targetPool = default;
        readonly EcsPoolInject<HandComponent> _handPool = default;
        readonly EcsPoolInject<HandTag> _handTagPool = default;
        readonly EcsPoolInject<GraveTag> _graveTagPool = default;
        readonly EcsPoolInject<TokenTag> _tokenTagPool = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var effectEntity in _filter.Value)
            {
                ref var effect = ref _discardPool.Value.Get(effectEntity);
                int targetEntity = _targetPool.Value.Get(effectEntity).TargetEntity;

                if (!_handPool.Value.Has(targetEntity))
                {
                    _world.Value.DelEntity(effectEntity);
                    continue;
                }

                ref var hand = ref _handPool.Value.Get(targetEntity);
                int toDiscard = System.Math.Min(effect.Count, hand.Count);

                for (int i = 0; i < toDiscard; i++)
                {
                    int idx = hand.Count - 1;
                    int cardEntity = hand.CardEntities[idx];
                    hand.CardEntities.RemoveAt(idx);
                    hand.Count--;

                    if (_handTagPool.Value.Has(cardEntity))
                        _handTagPool.Value.Del(cardEntity);

                    if (_tokenTagPool.Value.Has(cardEntity))
                        continue;

                    if (!_graveTagPool.Value.Has(cardEntity))
                        _graveTagPool.Value.Add(cardEntity);
                }

                _world.Value.DelEntity(effectEntity);
            }
        }
    }
}
