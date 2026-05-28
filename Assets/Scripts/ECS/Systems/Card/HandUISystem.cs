using Game.Core.Ecs.Components;
using Game.Core.Events;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using System.Collections.Generic;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Транслятор CardDrawnEvent → CardAddedToHandUIEvent для локального игрока.
    /// Слушает GameEventBus, проверяет что карта принадлежит локальному игроку,
    /// читает CardViewDataComponent и публикует событие для UI.
    /// </summary>
    public sealed class HandUISystem : IEcsInitSystem, IEcsRunSystem, IEcsDestroySystem, System.IDisposable
    {
        readonly EcsPoolInject<PlayerComponent>        _playerPool  = default;
        readonly EcsPoolInject<CardViewDataComponent>  _viewPool    = default;
        readonly EcsPoolInject<NetworkEntityComponent> _netKeyPool  = default;

        private readonly Queue<CardDrawnEvent> _pending = new Queue<CardDrawnEvent>();

        public void Init(IEcsSystems systems)
        {
            GameEventBus.Subscribe<CardDrawnEvent>(OnCardDrawn);
        }

        private void OnCardDrawn(CardDrawnEvent evt) => _pending.Enqueue(evt);

        public void Run(IEcsSystems systems)
        {
            while (_pending.Count > 0)
            {
                var evt = _pending.Dequeue();

                // Проверяем что игрок локальный
                int playerEntity = evt.PlayerId;
                if (!_playerPool.Value.Has(playerEntity)) continue;
                if (!_playerPool.Value.Get(playerEntity).IsLocalPlayer) continue;

                // Читаем визуальные данные карты
                int cardEntity = evt.CardEntity;
                if (!_viewPool.Value.Has(cardEntity)) continue;
                ref var view = ref _viewPool.Value.Get(cardEntity);

                string networkKey = string.Empty;
                if (_netKeyPool.Value.Has(cardEntity))
                    networkKey = _netKeyPool.Value.Get(cardEntity).NetworkEntityKey;

                GameEventBus.Publish(new CardAddedToHandUIEvent
                {
                    CardEntity  = cardEntity,
                    PlayerId    = _playerPool.Value.Get(playerEntity).PlayerId,
                    NetworkKey  = networkKey,
                    Icon        = view.ArtImage,
                    CardType    = view.CardType,
                    Element     = view.Element,
                    Rarity      = view.Rarity,
                    CardName    = view.CardName,
                    IsCommander = view.IsCommander,
                    Visual      = new Game.Core.Shared.CardVisualData
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

        public void Destroy(IEcsSystems systems) => Dispose();

        public void Dispose()
        {
            GameEventBus.Unsubscribe<CardDrawnEvent>(OnCardDrawn);
            _pending.Clear();
        }
    }
}
