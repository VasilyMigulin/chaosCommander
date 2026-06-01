using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;

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
        readonly EcsPoolInject<LocalComponent> _localPool = default;
        readonly EcsPoolInject<CardViewDataComponent> _viewPool = default;

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

                    if (_localPool.Value.Has(targetEntity))
                    {
                        var evt = new CardDiscardFromHandUIEvent { CardEntity = cardEntity };
                        if (_viewPool.Value.Has(cardEntity))
                        {
                            ref var view = ref _viewPool.Value.Get(cardEntity);
                            evt.CardName = view.CardName;
                            evt.Icon     = view.ArtImage;
                            evt.Visual   = new Game.Core.Shared.CardVisualData
                            {
                                CardName    = view.CardName,
                                Description = view.Description,
                                Icon        = view.ArtImage,
                                CardType    = view.CardType,
                                Rarity      = view.Rarity,
                                Element     = view.Element,
                                CostType    = view.CostType,
                                CostAmount  = view.CostAmount,
                                IsCreature  = view.IsCreature,
                                Attack      = view.Attack,
                                MaxHealth   = view.MaxHealth,
                                Speed       = view.Speed,
                                IsCommander = view.IsCommander,
                            };
                        }
                        GameEventBus.Publish(evt);
                    }
                }

                _world.Value.DelEntity(effectEntity);
            }
        }
    }
}
