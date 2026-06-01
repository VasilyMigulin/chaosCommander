using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Применяет FillHandEffectComponent: считает свободные слоты в руке владельца цели
    /// и публикует CreateCardEvent (InHand=true) на каждый слот. Командир в счёт не идёт
    /// (лимит — MaxNonCommanderCards).
    /// </summary>
    public sealed class ApplyFillHandSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsFilterInject<Inc<HitComponent, FillHandEffectComponent, TargetEntityComponent, EffectAbilityRefComponent>> _filter = default;
        readonly EcsPoolInject<FillHandEffectComponent> _fillPool = default;
        readonly EcsPoolInject<TargetEntityComponent> _targetPool = default;
        readonly EcsPoolInject<EffectAbilityRefComponent> _refPool = default;
        readonly EcsPoolInject<AbilitySourceComponent> _abilitySourcePool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;
        readonly EcsPoolInject<OwnCardTag> _ownCardPool = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsPoolInject<HandComponent> _handPool = default;
        readonly EcsPoolInject<CommanderTag> _commanderPool = default;
        readonly EcsPoolInject<NetworkEntityComponent> _netKeyPool = default;
        readonly EcsFilterInject<Inc<PlayerComponent, HandComponent>> _playerFilter = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var effectEntity in _filter.Value)
            {
                ref var data = ref _fillPool.Value.Get(effectEntity);
                int target = _targetPool.Value.Get(effectEntity).TargetEntity;
                int abilityEntity = _refPool.Value.Get(effectEntity).AbilityEntity;
                int sourceCard = _abilitySourcePool.Value.Has(abilityEntity)
                    ? _abilitySourcePool.Value.Get(abilityEntity).CardEntity : -1;

                int playerEntity = ResolvePlayerEntity(target);
                if (playerEntity < 0)
                {
                    _world.Value.DelEntity(effectEntity);
                    continue;
                }

                int ownerPlayerId = _playerPool.Value.Get(playerEntity).PlayerId;
                bool isOwn = sourceCard >= 0 && _ownCardPool.Value.Has(sourceCard);

                int freeSlots = CountFreeHandSlots(playerEntity);
                if (freeSlots <= 0)
                {
                    _world.Value.DelEntity(effectEntity);
                    continue;
                }

                string sourceKey = sourceCard >= 0 && _netKeyPool.Value.Has(sourceCard)
                    ? _netKeyPool.Value.Get(sourceCard).NetworkEntityKey
                    : ("local_" + System.Math.Max(0, sourceCard));

                for (int i = 0; i < freeSlots; i++)
                {
                    string key = $"{sourceKey}:fillhand:{abilityEntity}:{i}";
                    GameEventBus.Publish(new CreateCardEvent
                    {
                        ExpansionId      = data.ExpansionId,
                        CardId           = data.CardId,
                        NetworkEntityKey = key,
                        OwnerId          = ownerPlayerId,
                        IsEnemy          = !isOwn,
                        IsCommander      = false,
                        InHand           = true,
                    });
                }

                _world.Value.DelEntity(effectEntity);
            }
        }

        int ResolvePlayerEntity(int entity)
        {
            if (entity < 0) return -1;
            if (_playerPool.Value.Has(entity)) return entity;
            // Цель — карта/существо: ищем владельца.
            if (!_ownerPool.Value.Has(entity)) return -1;
            int ownerId = _ownerPool.Value.Get(entity).OwnerId;
            foreach (var pe in _playerFilter.Value)
                if (_playerPool.Value.Get(pe).PlayerId == ownerId) return pe;
            return -1;
        }

        int CountFreeHandSlots(int playerEntity)
        {
            if (!_handPool.Value.Has(playerEntity)) return 0;
            ref var hand = ref _handPool.Value.Get(playerEntity);

            int nonCommanderCount = 0;
            if (hand.CardEntities != null)
            {
                foreach (var ce in hand.CardEntities)
                {
                    if (_commanderPool.Value.Has(ce)) continue;
                    nonCommanderCount++;
                }
            }
            return System.Math.Max(0, HandComponent.MaxNonCommanderCards - nonCommanderCount);
        }
    }
}
