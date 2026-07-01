using Game.Core.Configs;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using System.Collections.Generic;
using Game.Core.Shared.Interface;
using UnityEngine;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Обрабатывает очередь CreateCardEvent:
    ///   — ищет CardModel в CardConfig по ExpansionId + CardId,
    ///   — вызывает CardModel.InitAndGetEntity(world),
    ///   — навешивает NetworkEntityComponent, DeckTag, OwnerComponent,
    ///   — ставит OwnCardTag или EnemyCardTag.
    ///
    /// CardConfig инжектируется через EcsCustomInject — ссылка на ScriptableObject
    /// остаётся в системном слое и никогда не попадает в компоненты.
    /// </summary>
    public sealed class CreateCardSystem : IEcsInitSystem, IEcsRunSystem, IEcsDestroySystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsCustomInject<CardConfig> _cardConfig = default;
        readonly EcsSharedInject<IGameStateContext> _state = default;
        readonly EcsPoolInject<NetworkEntityComponent> _netKeyPool = default;
        readonly EcsPoolInject<DeckTag> _deckTagPool = default;
        readonly EcsPoolInject<HandTag> _handTagPool = default;
        readonly EcsPoolInject<BoardTag> _boardTagPool = default;
        readonly EcsPoolInject<GraveTag> _graveTagPool = default;
        readonly EcsPoolInject<BoardPositionComponent> _boardPosPool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;
        readonly EcsPoolInject<OwnCardTag> _ownTagPool = default;
        readonly EcsPoolInject<EnemyCardTag> _enemyTagPool = default;
        readonly EcsPoolInject<HandComponent> _handPool = default;
        readonly EcsPoolInject<DeckComponent> _deckPool = default;
        readonly EcsPoolInject<AutoCastComponent> _autoCastPool = default;
        readonly EcsPoolInject<ForceRandomTargetingComponent> _forceRandomPool = default;

        // Буфер событий — GameEventBus не потокобезопасен, собираем за кадр
        readonly List<CreateCardEvent> _pending = new List<CreateCardEvent>();

        public void Init(IEcsSystems systems)
        {
            GameEventBus.Subscribe<CreateCardEvent>(OnCreateCard);
        }

        void OnCreateCard(CreateCardEvent evt) => _pending.Add(evt);

        public void Run(IEcsSystems systems)
        {
            if (_pending.Count == 0) return;

            foreach (var evt in _pending)
                ProcessEvent(evt);

            _pending.Clear();
        }

        void ProcessEvent(CreateCardEvent eventComp)
        {
            var instance = _cardConfig.Value.Get(eventComp.ExpansionId, eventComp.CardId);

            int cardEntity;

            if (instance?.CardData != null)
            {
                cardEntity = instance.CardData.Init(_world.Value, eventComp.PlayerOwnerEntity, eventComp.IsCommander);
            }
            else
            {
                Debug.LogWarning($"[CreateCardSystem] CardInstanceData not found: expansion='{eventComp.ExpansionId}' cardId={eventComp.CardId}. Creating stub entity.");
                cardEntity = _world.Value.NewEntity();
            }

            ref var net = ref _netKeyPool.Value.Add(cardEntity);
            net.NetworkEntityKey = eventComp.NetworkEntityKey;

            ref var owner = ref _ownerPool.Value.Add(cardEntity);
            owner.OwnerId = eventComp.OwnerId;
            owner.EntityKey = eventComp.NetworkEntityKey;

            if (eventComp.IsEnemy)
            { 
                _enemyTagPool.Value.Add(cardEntity);
            }
            else
            { 
                _ownTagPool.Value.Add(cardEntity);
            }

            if (eventComp.InBoard)
            {
                _boardTagPool.Value.Add(cardEntity);
                ref var pos = ref _boardPosPool.Value.Add(cardEntity);
                pos.Row = eventComp.BoardRow;
                pos.Col = eventComp.BoardCol;
                pos.OwnerId = eventComp.BoardOwnerId;
                // SpawnCreatureViewSystem подхватит BoardTag + BoardPosition и создаст вид.
                // Диагностика: для вида нужны CreatureTag + ViewRefComponent (их ставит CardCreatureModel.OnInit).
                // Если их нет — токен пришёл стабом (CardConfig не нашёл exp/cardId) или ассет токена не CreatureModel.
                if (!_world.Value.GetPool<CreatureTag>().Has(cardEntity)
                    || !_world.Value.GetPool<ViewRefComponent>().Has(cardEntity))
                    Debug.LogWarning($"[CreateCardSystem] InBoard entity={cardEntity} exp='{eventComp.ExpansionId}' cardId={eventComp.CardId} " +
                                     "БЕЗ CreatureTag/ViewRefComponent → вид не создастся. Токен не зарегистрирован в CardConfig или его ассет не CardCreatureModel.");
            }
            else if (eventComp.InGrave)
            {
                _graveTagPool.Value.Add(cardEntity);
            }
            else if (eventComp.InHand)
            {
                _handTagPool.Value.Add(cardEntity);
            }
            else
            {
                _deckTagPool.Value.Add(cardEntity);
            }

            _state.Value.AddEntity(cardEntity, localKey: cardEntity.ToString(), networkKey: eventComp.NetworkEntityKey);

            if (eventComp.AutoCast)
            {
                // Авто-розыгрыш (Фокус-покус): пометка для AutoCastSystem + форс Random-таргетинга (Йогг-Сарон).
                if (!_autoCastPool.Value.Has(cardEntity)) _autoCastPool.Value.Add(cardEntity);
                if (!_forceRandomPool.Value.Has(cardEntity)) _forceRandomPool.Value.Add(cardEntity);
            }

            if (eventComp.RegisterInZoneList)
                RegisterInZone(eventComp, cardEntity);
        }

        /// <summary>
        /// Добавляет порождённую карту в список зоны владельца (рука/колода) — обычное создание
        /// ставит только тег. Для руки локального владельца ещё публикует CardDrawnEvent (UI).
        /// </summary>
        void RegisterInZone(CreateCardEvent evt, int cardEntity)
        {
            int pe = evt.PlayerOwnerEntity;

            if (evt.InHand)
            {
                if (_handPool.Value.Has(pe))
                {
                    ref var hand = ref _handPool.Value.Get(pe);
                    hand.CardEntities ??= new List<int>();
                    hand.CardEntities.Add(cardEntity);
                    hand.Count = hand.CardEntities.Count;
                }
                if (!evt.IsEnemy)   // у врага своя отрисовка руки — не дублируем
                    GameEventBus.Publish(new CardDrawnEvent { CardEntity = cardEntity, PlayerId = pe });
            }
            else if (!evt.InBoard && !evt.InGrave)   // колода (else-ветка зоны)
            {
                if (_deckPool.Value.Has(pe))
                {
                    ref var deck = ref _deckPool.Value.Get(pe);
                    deck.CardEntities ??= new List<int>();
                    // «Втасовка»: позиция из ключа (детерминир. + синхронна на обоих), а не «в конец».
                    int idx = DeckShuffleUtil.InsertIndex(evt.NetworkEntityKey, deck.CardEntities.Count);
                    deck.CardEntities.Insert(idx, cardEntity);
                    deck.Count = deck.CardEntities.Count;
                }
            }
        }

        public void Destroy(IEcsSystems systems) => Dispose();

        public void Dispose()
        {
            GameEventBus.Unsubscribe<CreateCardEvent>(OnCreateCard);
        }
    }
}
