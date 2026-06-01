using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Mill: сбрасывает верхние N карт колоды целевого игрока в кладбище.
    /// Карты с TokenTag удаляются без помещения на кладбище.
    /// </summary>
    public sealed class ApplyMillSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsFilterInject<Inc<HitComponent, MillEffectComponent, TargetEntityComponent>> _filter = default;
        readonly EcsPoolInject<MillEffectComponent> _millPool = default;
        readonly EcsPoolInject<TargetEntityComponent> _targetPool = default;
        readonly EcsPoolInject<DeckComponent> _deckPool = default;
        readonly EcsPoolInject<DeckTag> _deckTagPool = default;
        readonly EcsPoolInject<GraveTag> _graveTagPool = default;
        readonly EcsPoolInject<TokenTag> _tokenTagPool = default;
        readonly EcsPoolInject<LocalComponent> _localPool = default;
        readonly EcsPoolInject<CardViewDataComponent> _viewPool = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var effectEntity in _filter.Value)
            {
                ref var effect = ref _millPool.Value.Get(effectEntity);
                int targetEntity = _targetPool.Value.Get(effectEntity).TargetEntity;

                if (!_deckPool.Value.Has(targetEntity))
                {
                    _world.Value.DelEntity(effectEntity);
                    continue;
                }

                ref var deck = ref _deckPool.Value.Get(targetEntity);
                int toMill = System.Math.Min(effect.Count, deck.Count);

                for (int i = 0; i < toMill; i++)
                {
                    int cardEntity = deck.CardEntities[0];

                    deck.CardEntities.RemoveAt(0);
                    deck.Count--;

                    if (_deckTagPool.Value.Has(cardEntity))
                        _deckTagPool.Value.Del(cardEntity);

                    if (_tokenTagPool.Value.Has(cardEntity))
                        continue;

                    if (!_graveTagPool.Value.Has(cardEntity))
                        _graveTagPool.Value.Add(cardEntity);

                    if (_localPool.Value.Has(targetEntity) && _viewPool.Value.Has(cardEntity))
                    {
                        ref var view = ref _viewPool.Value.Get(cardEntity);
                        Game.Core.Events.GameEventBus.Publish(new Game.Core.Events.CardMillFromDeckUIEvent
                        {
                            CardEntity = cardEntity,
                            CardName   = view.CardName,
                            Icon       = view.ArtImage,
                            Visual     = new Game.Core.Shared.CardVisualData
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
                            },
                        });
                    }
                }

                _world.Value.DelEntity(effectEntity);
            }
        }
    }
}
