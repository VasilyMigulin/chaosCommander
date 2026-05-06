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
    public sealed class CreateCardSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsCustomInject<CardConfig> _cardConfig = default;
        readonly EcsSharedInject<IGameStateContext> _state = default;
        readonly EcsPoolInject<NetworkEntityComponent> _netKeyPool = default;
        readonly EcsPoolInject<DeckTag> _deckTagPool = default;
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
                cardEntity = instance.CardData.InitAndGetEntity(_world.Value);
            }
            else
            {
                Debug.LogWarning($"[CreateCardSystem] CardInstanceData not found: expansion='{evt.ExpansionId}' cardId={evt.CardId}. Creating stub entity.");
                cardEntity = _world.Value.NewEntity();
            }

            if (!_netKeyPool.Value.Has(cardEntity))
            {
                ref var net = ref _netKeyPool.Value.Add(cardEntity);
                net.NetworkEntityKey = evt.EntityKey;
            }

            if (!_deckTagPool.Value.Has(cardEntity))
                _deckTagPool.Value.Add(cardEntity);

            if (!_ownerPool.Value.Has(cardEntity))
            {
                ref var owner = ref _ownerPool.Value.Add(cardEntity);
                owner.OwnerId   = evt.OwnerId;
                owner.EntityKey = evt.EntityKey;
            }

            if (evt.IsEnemy)
            {
                if (!_enemyTagPool.Value.Has(cardEntity))
                    _enemyTagPool.Value.Add(cardEntity);
            }
            else
            {
                if (!_ownTagPool.Value.Has(cardEntity))
                    _ownTagPool.Value.Add(cardEntity);
            }

            _state.Value.AddEntity(cardEntity, networkKey: evt.EntityKey);

            Debug.Log($"[CreateCardSystem] Created card entity={cardEntity} key='{evt.EntityKey}' expansion='{evt.ExpansionId}' cardId={evt.CardId} enemy={evt.IsEnemy}");
        }

        public void Dispose()
        {
            GameEventBus.Unsubscribe<CreateCardEvent>(OnCreateCard);
        }
    }
}
