using System;
using System.Collections.Generic;
using Game.Core.Configs;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Mono;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// Ставит косметический аватар ОППОНЕНТА по синку: OpponentAvatarId приезжает в CommandersRevealedUIEvent
    /// (его публикует RPC_RevealCommanders — avatarId едет тем же каналом, что и командир). Локального аватара
    /// ставит InitPlayerSystem из EquippedAvatar. Резолв здесь: Ecs видит Configs, а AvatarPlayerView (Mono)
    /// получает уже готовый префаб.
    ///
    /// Подписка в Init (не лениво в Run) — reveal приходит после сетевого round-trip, но так гарантированно
    /// не пропустим событие. В PvE reveal не публикуется → система просто ничего не делает.
    /// </summary>
    public sealed class SyncOpponentAvatarSystem : IEcsInitSystem, IEcsRunSystem, IEcsDestroySystem
    {
        readonly EcsFilterInject<Inc<RemoteComponent, AvatarViewComponent>> _remoteFilter = default;
        readonly EcsPoolInject<AvatarViewComponent> _avatarViewPool = default;

        readonly Queue<string> _pending = new Queue<string>();
        Action<CommandersRevealedUIEvent> _handler;

        public void Init(IEcsSystems systems)
        {
            _handler = e => { if (!string.IsNullOrEmpty(e.OpponentAvatarId)) _pending.Enqueue(e.OpponentAvatarId); };
            GameEventBus.Subscribe<CommandersRevealedUIEvent>(_handler);
        }

        public void Run(IEcsSystems systems)
        {
            while (_pending.Count > 0)
                Apply(_pending.Dequeue());
        }

        void Apply(string itemId)
        {
            var cfg = AvatarConfig.Instance;
            var avatar = cfg != null ? cfg.Get(itemId) : null;
            if (avatar == null || avatar.Prefab == null) return;

            // Оппонент = сущность с RemoteComponent; его аватар — AvatarViewComponent.View (GameObject аватара).
            foreach (var e in _remoteFilter.Value)
            {
                var view = _avatarViewPool.Value.Get(e).View;
                var apv = view != null ? view.GetComponent<AvatarPlayerView>() : null;
                apv?.SetAvatarVisual(avatar.Prefab);
            }
        }

        public void Destroy(IEcsSystems systems)
        {
            if (_handler != null) { GameEventBus.Unsubscribe<CommandersRevealedUIEvent>(_handler); _handler = null; }
        }
    }
}
