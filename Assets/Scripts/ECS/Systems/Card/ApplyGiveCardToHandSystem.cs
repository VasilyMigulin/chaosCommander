using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Применяет GiveCardToHandEffectComponent: публикует Count CreateCardEvent'ов
    /// с InHand=true для владельца TargetEntity. Если цель не игрок — пробуем взять
    /// OwnerComponent.OwnerId с цели-карты/существа.
    /// </summary>
    public sealed class ApplyGiveCardToHandSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsFilterInject<Inc<HitComponent, GiveCardToHandEffectComponent, TargetEntityComponent, EffectAbilityRefComponent>> _filter = default;
        readonly EcsPoolInject<GiveCardToHandEffectComponent> _givePool = default;
        readonly EcsPoolInject<TargetEntityComponent> _targetPool = default;
        readonly EcsPoolInject<EffectAbilityRefComponent> _refPool = default;
        readonly EcsPoolInject<AbilitySourceComponent> _abilitySourcePool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;
        readonly EcsPoolInject<OwnCardTag> _ownCardPool = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsPoolInject<NetworkEntityComponent> _netKeyPool = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var effectEntity in _filter.Value)
            {
                ref var data = ref _givePool.Value.Get(effectEntity);
                int target = _targetPool.Value.Get(effectEntity).TargetEntity;
                int abilityEntity = _refPool.Value.Get(effectEntity).AbilityEntity;
                int sourceCard = _abilitySourcePool.Value.Has(abilityEntity)
                    ? _abilitySourcePool.Value.Get(abilityEntity).CardEntity : -1;

                int ownerPlayerId = ResolveOwnerPlayerId(target);
                bool isOwn = sourceCard >= 0 && _ownCardPool.Value.Has(sourceCard);

                string sourceKey = sourceCard >= 0 && _netKeyPool.Value.Has(sourceCard)
                    ? _netKeyPool.Value.Get(sourceCard).NetworkEntityKey
                    : ("local_" + System.Math.Max(0, sourceCard));

                for (int i = 0; i < System.Math.Max(0, data.Count); i++)
                {
                    string key = $"{sourceKey}:give:{abilityEntity}:{i}";
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

        int ResolveOwnerPlayerId(int target)
        {
            if (target < 0) return -1;
            if (_playerPool.Value.Has(target)) return _playerPool.Value.Get(target).PlayerId;
            if (_ownerPool.Value.Has(target)) return _ownerPool.Value.Get(target).OwnerId;
            return -1;
        }
    }
}
