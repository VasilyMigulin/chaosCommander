using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Mono;
using Game.Core.Service;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Пушит HP/золото/ману игроков из ECS в их аватары каждый кадр (паттерн CreatureStatsViewSystem —
    /// дёшево, всегда актуально, без событий на каждое изменение).
    ///   • Враг: HP+ресурсы показываются над аватаром (AvatarPlayerView.SetStats).
    ///   • Локальный игрок: HP идёт в ресурс-панель — публикуем ResourceChangedEvent{Health} при изменении
    ///     (над-аватарные лейблы локального скрывает сам AvatarPlayerView).
    /// </summary>
    public sealed class PlayerStatsViewSystem : IEcsRunSystem
    {
        readonly EcsFilterInject<Inc<PlayerComponent, HealthComponent, AvatarViewComponent>> _filter = default;
        readonly EcsPoolInject<PlayerComponent>   _playerPool = default;
        readonly EcsPoolInject<HealthComponent>   _hpPool     = default;
        readonly EcsPoolInject<AvatarViewComponent> _avatarPool = default;
        readonly EcsPoolInject<GoldComponent>     _goldPool   = default;
        readonly EcsPoolInject<ManaComponent>     _manaPool   = default;

        int _lastLocalHp = int.MinValue, _lastLocalMaxHp = int.MinValue;

        public void Run(IEcsSystems systems)
        {
            foreach (var pe in _filter.Value)
            {
                ref var p  = ref _playerPool.Value.Get(pe);
                ref var hp = ref _hpPool.Value.Get(pe);
                int gold = _goldPool.Value.Has(pe) ? _goldPool.Value.Get(pe).Current : 0;
                int mana = _manaPool.Value.Has(pe) ? _manaPool.Value.Get(pe).Current : 0;

                var go = _avatarPool.Value.Get(pe).View;
                var view = go != null ? go.GetComponent<AvatarPlayerView>() : null;
                view?.SetStats(hp.Current, hp.Max, gold, mana, p.IsLocalPlayer);

                if (p.IsLocalPlayer && (hp.Current != _lastLocalHp || hp.Max != _lastLocalMaxHp))
                {
                    _lastLocalHp = hp.Current;
                    _lastLocalMaxHp = hp.Max;
                    GameEventBus.Publish(new ResourceChangedEvent
                    {
                        isLocalPlayer = true,
                        Type          = EnumService.ResourceType.Health,
                        NewValue      = hp.Current,
                        MaxValue      = hp.Max,
                    });
                }
            }
        }
    }
}
