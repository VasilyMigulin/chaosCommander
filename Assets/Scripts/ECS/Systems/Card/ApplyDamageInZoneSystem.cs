using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Применяет DamageInZoneEffectComponent: пишет TakeDamageEvent на все
    /// карты/существа в указанных зонах владельца TargetEntity.
    /// Для карт-в-колоде/руке: TakeDamageEvent применится TakeDamageSystem'ом
    /// (понизит Current HP в Base/BaseMax значениях карты).
    /// </summary>
    public sealed class ApplyDamageInZoneSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsFilterInject<Inc<HitComponent, DamageInZoneEffectComponent, TargetEntityComponent>> _filter = default;
        readonly EcsPoolInject<DamageInZoneEffectComponent> _dmgPool = default;
        readonly EcsPoolInject<TargetEntityComponent> _targetPool = default;
        readonly EcsPoolInject<DeckComponent> _deckPool = default;
        readonly EcsPoolInject<HandComponent> _handPool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsPoolInject<CreatureTag> _creaturePool = default;
        readonly EcsPoolInject<TakeDamageEvent> _takeDmgPool = default;
        readonly EcsFilterInject<Inc<PlayerComponent>> _playerFilter = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var effectEntity in _filter.Value)
            {
                ref var data = ref _dmgPool.Value.Get(effectEntity);
                int target = _targetPool.Value.Get(effectEntity).TargetEntity;

                int playerEntity = ResolvePlayer(target);
                if (playerEntity >= 0)
                    Apply(playerEntity, data);

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

        void Apply(int playerEntity, in DamageInZoneEffectComponent data)
        {
            int ownerPlayerId = _playerPool.Value.Get(playerEntity).PlayerId;

            if ((data.Zones & BuffZone.Deck) != 0 && _deckPool.Value.Has(playerEntity))
            {
                ref var deck = ref _deckPool.Value.Get(playerEntity);
                if (deck.CardEntities != null)
                    foreach (var ce in deck.CardEntities) Hit(ce, data);
            }
            if ((data.Zones & BuffZone.Hand) != 0 && _handPool.Value.Has(playerEntity))
            {
                ref var hand = ref _handPool.Value.Get(playerEntity);
                if (hand.CardEntities != null)
                    foreach (var ce in hand.CardEntities) Hit(ce, data);
            }
            if ((data.Zones & BuffZone.Grave) != 0)
            {
                foreach (var ce in _world.Value.Filter<GraveTag>().Inc<OwnerComponent>().End())
                {
                    if (_ownerPool.Value.Get(ce).OwnerId != ownerPlayerId) continue;
                    Hit(ce, data);
                }
            }
            if ((data.Zones & BuffZone.Board) != 0)
            {
                foreach (var ce in _world.Value.Filter<BoardTag>().Inc<OwnerComponent>().End())
                {
                    if (_ownerPool.Value.Get(ce).OwnerId != ownerPlayerId) continue;
                    Hit(ce, data);
                }
            }
        }

        void Hit(int ce, in DamageInZoneEffectComponent data)
        {
            if (data.CreatureOnly && !_creaturePool.Value.Has(ce)) return;
            if (_takeDmgPool.Value.Has(ce))
            {
                ref var existing = ref _takeDmgPool.Value.Get(ce);
                existing.Amount += data.Amount;
            }
            else
            {
                ref var dmg = ref _takeDmgPool.Value.Add(ce);
                dmg.Amount = data.Amount;
                dmg.Attacker = -1;
            }
        }
    }
}
