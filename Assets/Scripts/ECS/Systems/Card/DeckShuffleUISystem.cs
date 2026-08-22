using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Mono;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using System.Collections.Generic;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Транслятор CardShuffledToDeckEvent → CardShuffledToDeckUIEvent — зеркало HandUISystem, только для
    /// колоды: резолвит мировую позицию источника (SourceEntity/FromWorld) в экранную ЗДЕСЬ (ECS/Mono-слой,
    /// UI-сборка камеру не референсит), читает CardViewDataComponent, публикует УЖЕ готовый для UI кадр.
    /// </summary>
    public sealed class DeckShuffleUISystem : IEcsInitSystem, IEcsRunSystem, IEcsDestroySystem, System.IDisposable
    {
        readonly EcsWorldInject _world = default;
        readonly EcsCustomInject<BoardView> _boardView = default;
        readonly EcsPoolInject<PlayerComponent>       _playerPool = default;
        readonly EcsPoolInject<CardViewDataComponent> _viewPool   = default;

        readonly Queue<CardShuffledToDeckEvent> _pending = new Queue<CardShuffledToDeckEvent>();
        bool _subscribed;

        public void Init(IEcsSystems systems)
        {
            GameEventBus.Subscribe<CardShuffledToDeckEvent>(OnShuffled);
            _subscribed = true;
        }

        void OnShuffled(CardShuffledToDeckEvent evt) => _pending.Enqueue(evt);

        public void Run(IEcsSystems systems)
        {
            while (_pending.Count > 0)
            {
                var evt = _pending.Dequeue();

                int playerEntity = evt.PlayerId;
                if (!_playerPool.Value.Has(playerEntity)) continue;
                if (!_playerPool.Value.Get(playerEntity).IsLocalPlayer) continue;

                int cardEntity = evt.CardEntity;
                if (!_viewPool.Value.Has(cardEntity)) continue;
                ref var view = ref _viewPool.Value.Get(cardEntity);

                // Та же логика, что у HandUISystem: FromWorld — готовая позиция (источник сам ушёл с поля);
                // иначе SourceEntity лениво — обычно кастер (Старый колдун) ещё стоит на столе. У кастера
                // без борд-позиции (спелл — «Дать газу!») EntityWorldPosUtil сам провалится в OwnerComponent →
                // аватар владельца, «летит от игрока» — отдельно ничего решать не нужно.
                UnityEngine.Vector3? fromWorld = evt.FromWorld;
                if (fromWorld == null && evt.SourceEntity is int src
                    && EntityWorldPosUtil.TryGet(_world.Value, _boardView.Value, src, out var srcPos))
                    fromWorld = srcPos;

                UnityEngine.Vector2? fromScreen = null;
                if (fromWorld.HasValue)
                {
                    var boardCam = Game.Core.Mono.BattleCameraSelector.ActiveCamera != null
                        ? Game.Core.Mono.BattleCameraSelector.ActiveCamera
                        : UnityEngine.Camera.main;
                    if (boardCam != null)
                        fromScreen = UnityEngine.RectTransformUtility.WorldToScreenPoint(boardCam, fromWorld.Value);
                }

                GameEventBus.Publish(new CardShuffledToDeckUIEvent
                {
                    CardEntity = cardEntity,
                    CardName   = view.CardName,
                    Icon       = view.ArtImage,
                    Visual     = view.ToVisual(),
                    FromScreen = fromScreen,
                });
            }
        }

        public void Destroy(IEcsSystems systems) => Dispose();

        public void Dispose()
        {
            if (!_subscribed) return;
            GameEventBus.Unsubscribe<CardShuffledToDeckEvent>(OnShuffled);
            _subscribed = false;
        }
    }
}
