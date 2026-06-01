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

        void ProcessEvent(CreateCardEvent evt)
        {
            var instance = _cardConfig.Value.Get(evt.ExpansionId, evt.CardId);

            int cardEntity;

            if (instance?.CardData != null)
            {
                cardEntity = instance.CardData.InitAndGetEntity(_world.Value, evt.IsCommander);
            }
            else
            {
                Debug.LogWarning($"[CreateCardSystem] CardInstanceData not found: expansion='{evt.ExpansionId}' cardId={evt.CardId}. Creating stub entity.");
                cardEntity = _world.Value.NewEntity();
            }

            ref var net = ref _netKeyPool.Value.Add(cardEntity);
            net.NetworkEntityKey = evt.NetworkEntityKey;

            ref var owner = ref _ownerPool.Value.Add(cardEntity);
            owner.OwnerId = evt.OwnerId;
            owner.EntityKey = evt.NetworkEntityKey;

            if (evt.IsEnemy)
            { 
                _enemyTagPool.Value.Add(cardEntity);
            }
            else
            { 
                _ownTagPool.Value.Add(cardEntity);
            }

            if (evt.InBoard)
            {
                _boardTagPool.Value.Add(cardEntity);
                ref var pos = ref _boardPosPool.Value.Add(cardEntity);
                pos.Row = evt.BoardRow;
                pos.Col = evt.BoardCol;
                pos.OwnerId = evt.BoardOwnerId;
                // SpawnCreatureViewSystem подхватит BoardTag + BoardPosition и создаст вид.
            }
            else if (evt.InGrave)
            {
                _graveTagPool.Value.Add(cardEntity);
            }
            else if (evt.InHand)
            {
                _handTagPool.Value.Add(cardEntity);
            }
            else
            {
                _deckTagPool.Value.Add(cardEntity);
            }

            _state.Value.AddEntity(cardEntity, localKey: cardEntity.ToString(), networkKey: evt.NetworkEntityKey);

            Debug.Log($"[CreateCardSystem] Created card entity={cardEntity} key='{evt.NetworkEntityKey}' expansion='{evt.ExpansionId}' cardId={evt.CardId} enemy={evt.IsEnemy}");
        }

        public void Destroy(IEcsSystems systems) => Dispose();

        public void Dispose()
        {
            GameEventBus.Unsubscribe<CreateCardEvent>(OnCreateCard);
        }
    }
}
