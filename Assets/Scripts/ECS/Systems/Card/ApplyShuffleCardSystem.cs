using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Service;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Замешивает карту из ShuffleCardEffectComponent в колоду целевого игрока.
    ///
    /// Фильтрует effect entity (EffectComponent + ShuffleCardEffectComponent).
    /// Определяет цель (своя/чужая колода) через OwnerEntity + PlayerSideComponent —
    /// детерминировано на обоих клиентах, т.к. Side фиксирован глобально (1=хост, 2=гость).
    /// </summary>
    public sealed class ApplyShuffleCardSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsFilterInject<Inc<HitComponent, ShuffleCardEffectComponent, TargetEntityComponent>> _filter = default;
        readonly EcsFilterInject<Inc<DeckComponent, PlayerSideComponent>> _allPlayersFilter = default;
        readonly EcsPoolInject<ShuffleCardEffectComponent> _shufflePool = default;
        readonly EcsPoolInject<TargetEntityComponent> _targetPool = default;
        readonly EcsPoolInject<DeckComponent> _deckPool = default;
        readonly EcsPoolInject<DeckTag> _deckTagPool = default;
        readonly EcsPoolInject<PlayerSideComponent> _sidePool = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var effectEntity in _filter.Value)
            {
                ref var effectComp = ref _shufflePool.Value.Get(effectEntity);
                int ownerEntity = _targetPool.Value.Get(effectEntity).OwnerEntity;

                int ownerSide = _sidePool.Value.Has(ownerEntity)
                    ? _sidePool.Value.Get(ownerEntity).Side
                    : 1;

                foreach (var cardData in effectComp.Cards)
                {
                    int targetPlayerEntity = cardData.IntoOpponentDeck
                        ? FindPlayerBySide(ownerSide == 1 ? 2 : 1)
                        : FindPlayerBySide(ownerSide);

                    if (targetPlayerEntity < 0)
                        continue;

                    ref var deckComp = ref _deckPool.Value.Get(targetPlayerEntity);

                    for (int i = 0; i < cardData.ShuffleCount; i++)
                    {
                        int newCardEntity = cardData.CardToShuffle.Shuffle(_world.Value, targetPlayerEntity);
                        _deckTagPool.Value.Add(newCardEntity);

                        int insertAt = deckComp.Count / 2;
                        deckComp.CardEntities.Insert(insertAt, newCardEntity);
                        deckComp.Count++;
                    }

                    deckComp.CardEntities.Shuffle();
                }

                _world.Value.DelEntity(effectEntity);
            }
        }

        int FindPlayerBySide(int side)
        {
            foreach (var entity in _allPlayersFilter.Value)
            {
                if (_sidePool.Value.Get(entity).Side == side)
                    return entity;
            }
            return -1;
        }
    }
}
