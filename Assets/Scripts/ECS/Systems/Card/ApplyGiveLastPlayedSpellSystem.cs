using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Применяет GiveLastPlayedSpellToHandEffectComponent: ищет у владельца источника
    /// LastPlayedSpellComponent и публикует CreateCardEvent (InHand=true) с этой моделью.
    /// </summary>
    public sealed class ApplyGiveLastPlayedSpellSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsFilterInject<Inc<HitComponent, GiveLastPlayedSpellToHandEffectComponent, EffectAbilityRefComponent>> _filter = default;
        readonly EcsPoolInject<EffectAbilityRefComponent> _refPool = default;
        readonly EcsPoolInject<AbilitySourceComponent> _abilitySourcePool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;
        readonly EcsPoolInject<OwnCardTag> _ownCardPool = default;
        readonly EcsPoolInject<LastPlayedSpellComponent> _lastPool = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsPoolInject<NetworkEntityComponent> _netKeyPool = default;
        readonly EcsFilterInject<Inc<PlayerComponent>> _playerFilter = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var effectEntity in _filter.Value)
            {
                int abilityEntity = _refPool.Value.Get(effectEntity).AbilityEntity;
                int sourceCard = _abilitySourcePool.Value.Has(abilityEntity)
                    ? _abilitySourcePool.Value.Get(abilityEntity).CardEntity : -1;

                if (sourceCard >= 0)
                {
                    int ownerId = _ownerPool.Value.Has(sourceCard)
                        ? _ownerPool.Value.Get(sourceCard).OwnerId : -1;
                    int playerEntity = FindPlayer(ownerId);
                    bool isOwn = _ownCardPool.Value.Has(sourceCard);

                    if (playerEntity >= 0 && _lastPool.Value.Has(playerEntity))
                    {
                        ref var last = ref _lastPool.Value.Get(playerEntity);
                        if (last.HasValue)
                        {
                            string sourceKey = _netKeyPool.Value.Has(sourceCard)
                                ? _netKeyPool.Value.Get(sourceCard).NetworkEntityKey
                                : ("local_" + sourceCard);
                            GameEventBus.Publish(new CreateCardEvent
                            {
                                ExpansionId      = last.ExpansionId,
                                CardId           = last.ModelId,
                                NetworkEntityKey = $"{sourceKey}:lastspell:{abilityEntity}",
                                OwnerId          = ownerId,
                                IsEnemy          = !isOwn,
                                InHand           = true,
                            });
                        }
                    }
                }

                _world.Value.DelEntity(effectEntity);
            }
        }

        int FindPlayer(int playerId)
        {
            foreach (var pe in _playerFilter.Value)
                if (_playerPool.Value.Get(pe).PlayerId == playerId) return pe;
            return -1;
        }
    }
}
