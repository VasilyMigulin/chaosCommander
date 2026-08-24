using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Mono;
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
        readonly EcsWorldInject _world = default;
        readonly EcsCustomInject<BoardView> _boardView = default;
        readonly EcsPoolInject<PlayerComponent>        _playerPool  = default;
        readonly EcsPoolInject<CardViewDataComponent>  _viewPool    = default;
        readonly EcsPoolInject<NetworkEntityComponent> _netKeyPool  = default;
        readonly EcsPoolInject<GoldCostComponent>   _goldCostPool   = default;
        readonly EcsPoolInject<ManaCostComponent>   _manaCostPool   = default;
        readonly EcsPoolInject<HealthCostComponent> _healthCostPool = default;

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

                int costAmount = EffectiveCost(cardEntity, playerEntity, view.CostAmount);

                // Откуда карта визуально «прилетела»: FromWorld — уже готовая позиция (источник САМ ушёл с
                // поля — баунс/командир, снимать её нужно было раньше, до этого события); иначе пробуем
                // SourceEntity лениво (раскопка — кастер обычно ещё стоит на столе). Ни то ни другое —
                // обычный добор, CardLayout летит со своей дефолтной точки. Проецируем В ЭКРАННЫЕ координаты
                // ЗДЕСЬ (не в CardLayout): UI-сборка не референсит Mono/BattleCameraSelector, а мировая
                // позиция сама по себе для неё бесполезна без камеры.
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

                GameEventBus.Publish(new CardAddedToHandUIEvent
                {
                    CardEntity  = cardEntity,
                    PlayerId    = _playerPool.Value.Get(playerEntity).PlayerId,
                    NetworkKey  = networkKey,
                    FromScreen  = fromScreen,
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
                        CostAmount  = costAmount,          // ЖИВАЯ эффективная цена (см. EffectiveCost ниже)
                        HasBaseCost = true,
                        BaseCostAmount = view.CostAmount,  // печатная база (снимок CardModel.Init) — для окраски
                        IsCreature  = view.IsCreature,
                        Attack      = view.Attack,
                        MaxHealth   = view.MaxHealth,
                        Speed       = view.Speed,
                        IsCommander = view.IsCommander,
                    },
                });
            }
        }

        // Живая эффективная стоимость (GoldCostComponent.Cost и т.п. — уже Base+модификаторы, см.
        // GoldCostComponent.RecalculateValue) + модификатор владельца (Гиперинфляция, CostModifierUtil) —
        // как CardAffordabilitySystem.TryEffectiveCost. CardViewDataComponent.CostAmount (fallback) — застывший
        // снимок на момент CardModel.Init, ДО модификаторов Discover/баффов (Мастер над чарами: BuffCost
        // применяется к только что созданной карте РАНЬШЕ CardDrawnEvent, но снимок этого не видит) — поэтому
        // читаем актуальный кост-компонент, а не снимок.
        int EffectiveCost(int cardEntity, int playerEntity, int fallback)
        {
            if (_goldCostPool.Value.Has(cardEntity))
                return CostModifierUtil.Effective(_world.Value, playerEntity, cardEntity, _goldCostPool.Value.Get(cardEntity).Cost);
            if (_manaCostPool.Value.Has(cardEntity))
                return CostModifierUtil.Effective(_world.Value, playerEntity, cardEntity, _manaCostPool.Value.Get(cardEntity).Cost);
            if (_healthCostPool.Value.Has(cardEntity))
                return CostModifierUtil.Effective(_world.Value, playerEntity, cardEntity, _healthCostPool.Value.Get(cardEntity).Cost);
            return fallback;
        }

        public void Destroy(IEcsSystems systems) => Dispose();

        public void Dispose()
        {
            GameEventBus.Unsubscribe<CardDrawnEvent>(OnCardDrawn);
            _pending.Clear();
        }
    }
}
