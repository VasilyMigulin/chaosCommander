using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Применяет BuffDeckCardsEffectComponent: пишет +Atk/+Hp/+Speed в Base/BaseMax
    /// всем картам в указанных зонах владельца TargetEntity (обычно игрока).
    /// Карты на доске получают бонус сразу через Base/BaseMax + AuraRecalc.
    /// </summary>
    public sealed class ApplyBuffDeckCardsSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsFilterInject<Inc<HitComponent, BuffDeckCardsEffectComponent, TargetEntityComponent>> _filter = default;
        readonly EcsPoolInject<BuffDeckCardsEffectComponent> _buffPool = default;
        readonly EcsPoolInject<TargetEntityComponent> _targetPool = default;
        readonly EcsPoolInject<DeckComponent> _deckPool = default;
        readonly EcsPoolInject<HandComponent> _handPool = default;
        readonly EcsPoolInject<HandTag> _handTagPool = default;
        readonly EcsPoolInject<DeckTag> _deckTagPool = default;
        readonly EcsPoolInject<GraveTag> _graveTagPool = default;
        readonly EcsPoolInject<BoardTag> _boardTagPool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsPoolInject<AttackComponent> _attackPool = default;
        readonly EcsPoolInject<HealthComponent> _hpPool = default;
        readonly EcsPoolInject<SpeedComponent> _speedPool = default;
        readonly EcsFilterInject<Inc<PlayerComponent>> _playerFilter = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var effectEntity in _filter.Value)
            {
                ref var buff = ref _buffPool.Value.Get(effectEntity);
                int target = _targetPool.Value.Get(effectEntity).TargetEntity;

                int playerEntity = ResolvePlayer(target);
                if (playerEntity >= 0)
                    ApplyToPlayer(playerEntity, buff);

                _world.Value.DelEntity(effectEntity);
            }
        }

        int ResolvePlayer(int target)
        {
            if (target < 0) return -1;
            if (_playerPool.Value.Has(target)) return target;
            if (!_ownerPool.Value.Has(target)) return -1;
            int ownerId = _ownerPool.Value.Get(target).OwnerId;
            foreach (var pe in _playerFilter.Value)
                if (_playerPool.Value.Get(pe).PlayerId == ownerId) return pe;
            return -1;
        }

        void ApplyToPlayer(int playerEntity, in BuffDeckCardsEffectComponent buff)
        {
            int ownerPlayerId = _playerPool.Value.Get(playerEntity).PlayerId;

            if ((buff.Zones & BuffZone.Deck) != 0 && _deckPool.Value.Has(playerEntity))
            {
                ref var deck = ref _deckPool.Value.Get(playerEntity);
                if (deck.CardEntities != null)
                    foreach (var ce in deck.CardEntities) BumpStats(ce, buff);
            }
            if ((buff.Zones & BuffZone.Hand) != 0 && _handPool.Value.Has(playerEntity))
            {
                ref var hand = ref _handPool.Value.Get(playerEntity);
                if (hand.CardEntities != null)
                    foreach (var ce in hand.CardEntities) BumpStats(ce, buff);
            }
            if ((buff.Zones & BuffZone.Grave) != 0)
            {
                foreach (var ce in _world.Value.Filter<GraveTag>().Inc<OwnerComponent>().End())
                {
                    if (_ownerPool.Value.Get(ce).OwnerId != ownerPlayerId) continue;
                    BumpStats(ce, buff);
                }
            }
            if ((buff.Zones & BuffZone.Board) != 0)
            {
                foreach (var ce in _world.Value.Filter<BoardTag>().Inc<OwnerComponent>().End())
                {
                    if (_ownerPool.Value.Get(ce).OwnerId != ownerPlayerId) continue;
                    BumpStats(ce, buff);
                }
            }
        }

        void BumpStats(int ce, in BuffDeckCardsEffectComponent buff)
        {
            if (buff.AttackBonus != 0 && _attackPool.Value.Has(ce))
            {
                ref var a = ref _attackPool.Value.Get(ce);
                a.Base += buff.AttackBonus; a.Value += buff.AttackBonus;
            }
            if (buff.HealthBonus != 0 && _hpPool.Value.Has(ce))
            {
                ref var h = ref _hpPool.Value.Get(ce);
                h.BaseMax += buff.HealthBonus; h.Max += buff.HealthBonus; h.Current += buff.HealthBonus;
            }
            if (buff.SpeedBonus != 0 && _speedPool.Value.Has(ce))
            {
                ref var s = ref _speedPool.Value.Get(ce);
                s.BaseMax += buff.SpeedBonus; s.Max += buff.SpeedBonus;
            }
        }
    }
}
