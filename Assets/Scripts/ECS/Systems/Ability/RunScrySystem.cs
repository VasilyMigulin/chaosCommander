using System.Collections.Generic;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Network;
using Game.Core.Configs;
using Game.Core.Instance.Card;
using Game.Core.Shared;
using Game.Core.Shared.Interface;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// Предсказание-ЭФФЕКТ (ScryEffect, NonTarget): посмотреть LookCount верхних карт колоды, выбранную
    /// оставить НАВЕРХУ, остальные — в КОНЕЦ колоды (в исходном относительном порядке). Структурно —
    /// близнец RunDiscoverSystem (тот же общий канал окна: PickupWindow/CardPickBrokerSystem/
    /// CardPickResolvedNetEvent), но терминал проще: ничего не меняет зону/владельца и не создаёт новых
    /// сущностей — только переставляет то, что уже в DeckComponent.CardEntities.
    ///   • Своя карта (OwnCardTag) → показать окно (CardPickOfferedEvent), ждать CardPickChosenEvent.
    ///   • Чужая (реплей) → авто-резолв из CardPickReplayStore по NetKey источника; «остальные» пересчитываются
    ///     локально (верх СВОЕЙ колоды в момент резолва — зеркален активу, т.к. порядок колоды синхронен,
    ///     а между офером и резолвом с этой колодой ничего больше не происходит — HasEarlierPending гарантирует
    ///     строгую очередь запросов одного источника).
    /// </summary>
    public sealed class RunScrySystem : IEcsInitSystem, IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;

        readonly EcsFilterInject<Inc<ScryRequestComponent>> _reqFilter = default;
        readonly EcsPoolInject<ScryRequestComponent> _reqPool = default;

        readonly EcsPoolInject<OwnCardTag>             _ownPool    = default;
        readonly EcsPoolInject<ActiveState>            _activePool = default;
        readonly EcsPoolInject<NetworkEntityComponent> _netPool    = default;
        readonly EcsPoolInject<CardModelComponent>     _modelPool  = default;
        readonly EcsCustomInject<CardConfig>           _cardConfig = default;

        readonly EcsPoolInject<DeckComponent>   _deckPool   = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsSharedInject<IGameStateContext> _state  = default;

        readonly Queue<CardPickChosenEvent>    _chosen    = new Queue<CardPickChosenEvent>();
        readonly Queue<CardPickCancelledEvent> _cancelled = new Queue<CardPickCancelledEvent>();
        readonly Queue<int> _forced  = new Queue<int>();   // reqEntity: предсказание без окна (авто-ролл)
        readonly Queue<int> _expired = new Queue<int>();   // PlayerId, чей ход закончился

        public void Init(IEcsSystems systems)
        {
            GameEventBus.Subscribe<CardPickChosenEvent>(e => _chosen.Enqueue(e));
            GameEventBus.Subscribe<CardPickCancelledEvent>(e => _cancelled.Enqueue(e));
            GameEventBus.Subscribe<CardPickExpiredEvent>(e => _expired.Enqueue(e.PlayerId));
        }

        public void Run(IEcsSystems systems)
        {
            bool simulator = TurnGate.IsLocalActive(_world.Value);

            var pending = new List<int>();
            foreach (var req in _reqFilter.Value) pending.Add(req);
            foreach (var req in pending)
            {
                if (!_reqPool.Value.Has(req)) continue;
                if (simulator) TryDecide(req);
                else           TryResolveRemote(req);
            }

            while (_chosen.Count > 0)    ResolveChosen(_chosen.Dequeue());
            while (_forced.Count > 0)    ForceResolve(_forced.Dequeue());
            while (_cancelled.Count > 0) ResolveCancelled(_cancelled.Dequeue());
            while (_expired.Count > 0)   ExpireForPlayer(_expired.Dequeue());
        }

        void ExpireForPlayer(int playerId)
        {
            var pending = new List<int>();
            foreach (var req in _reqFilter.Value) pending.Add(req);
            foreach (var req in pending)
            {
                if (!_reqPool.Value.Has(req)) continue;
                ref var r = ref _reqPool.Value.Get(req);
                if (!_ownPool.Value.Has(r.SourceCardEntity)) continue;
                if (!_playerPool.Value.Has(r.OwnerPlayerEntity) || _playerPool.Value.Get(r.OwnerPlayerEntity).PlayerId != playerId) continue;
                ForceResolve(req);
            }
        }

        void ForceResolve(int reqEntity)
        {
            if (!_reqPool.Value.Has(reqEntity)) return;
            ref var r = ref _reqPool.Value.Get(reqEntity);

            if (!r.Offered)
            {
                BuildTopOffer(ref r, out var tokens, out var exp, out var ids);
                if (tokens.Length == 0) { _world.Value.DelEntity(reqEntity); return; }
                r.Offered = true; r.ShownTokens = tokens; r.ShownExp = exp; r.ShownCardId = ids;
            }
            if (r.ShownTokens == null || r.ShownTokens.Length == 0) { _world.Value.DelEntity(reqEntity); return; }

            Apply(reqEntity, r.ShownTokens[UnityEngine.Random.Range(0, r.ShownTokens.Length)]);
        }

        bool IsOwnersTurn(int playerEntity)
        {
            if (playerEntity < 0) return false;
            return _activePool.Value.Has(playerEntity)
                || _world.Value.GetPool<StartTurnState>().Has(playerEntity)
                || _world.Value.GetPool<EndTurnState>().Has(playerEntity);
        }

        bool HasEarlierPending(int reqEntity)
        {
            ref var r = ref _reqPool.Value.Get(reqEntity);
            foreach (var other in _reqFilter.Value)
            {
                if (other == reqEntity || !_reqPool.Value.Has(other)) continue;
                ref var o = ref _reqPool.Value.Get(other);
                if (o.SourceCardEntity == r.SourceCardEntity && o.Seq < r.Seq) return true;
            }
            return false;
        }

        void TryDecide(int reqEntity)
        {
            ref var r = ref _reqPool.Value.Get(reqEntity);
            if (r.Offered) return;
            if (HasEarlierPending(reqEntity)) return;

            bool autoRoll = _world.Value.GetPool<ForceRandomTargetingComponent>().Has(r.SourceCardEntity)
                         || !_ownPool.Value.Has(r.SourceCardEntity)
                         || !IsOwnersTurn(r.OwnerPlayerEntity);

            if (!autoRoll && !_activePool.Value.Has(r.OwnerPlayerEntity)) return;

            if (!autoRoll && !PickTicket.Ready(_world.Value, reqEntity, ref r.RequestId, r.OwnerPlayerEntity))
                return;

            BuildTopOffer(ref r, out var tokens, out var exp, out var ids);
            if (tokens.Length == 0) { _world.Value.DelEntity(reqEntity); return; }

            r.Offered     = true;
            r.ShownTokens = tokens;
            r.ShownExp    = exp;
            r.ShownCardId = ids;

            if (autoRoll) { _forced.Enqueue(reqEntity); return; }

            GameEventBus.Publish(new CardPickOfferedEvent
            {
                RequestId           = r.RequestId,
                CastingCardEntity   = r.SourceCardEntity,
                PlayerEntity        = r.OwnerPlayerEntity,
                OfferedCardEntities = tokens,
                OfferedCardVisuals  = BuildVisuals(tokens),
                OfferedCount        = tokens.Length,
                AllowCancel         = false,   // предсказание — все 3 карты в любом случае учтены, отменять нечего
            });
        }

        // Верхние LookCount карт колоды владельца, в порядке «от верха» (индекс 0 списка = верх, как у
        // DrawCardSystem/PlayTopDeckCardEffect).
        void BuildTopOffer(ref ScryRequestComponent r, out int[] tokens, out string[] exp, out int[] ids)
        {
            var deckPool = _deckPool.Value;
            if (!deckPool.Has(r.OwnerPlayerEntity) || deckPool.Get(r.OwnerPlayerEntity).CardEntities == null)
            {
                tokens = System.Array.Empty<int>(); exp = System.Array.Empty<string>(); ids = System.Array.Empty<int>();
                return;
            }
            ref var deck = ref deckPool.Get(r.OwnerPlayerEntity);
            int n = System.Math.Min(r.LookCount, deck.CardEntities.Count);
            tokens = new int[n]; exp = new string[n]; ids = new int[n];
            for (int i = 0; i < n; i++)
            {
                int e = deck.CardEntities[i];
                tokens[i] = e;
                if (_modelPool.Value.Has(e))
                {
                    ref var m = ref _modelPool.Value.Get(e);
                    exp[i] = m.ExpansionId; ids[i] = m.ModelId;
                }
            }
        }

        CardVisualData[] BuildVisuals(int[] tokens)
        {
            var visuals = new CardVisualData[tokens.Length];
            for (int i = 0; i < tokens.Length; i++)
            {
                if (!_modelPool.Value.Has(tokens[i])) continue;
                ref var m = ref _modelPool.Value.Get(tokens[i]);
                var inst = _cardConfig.Value.Get(m.ExpansionId, m.ModelId);
                if (inst?.CardData != null) visuals[i] = CardVisualDataFactory.From(inst.CardData);
            }
            return visuals;
        }

        void ResolveChosen(CardPickChosenEvent e)
        {
            if (e.RequestId == 0) return;
            foreach (var reqEntity in _reqFilter.Value)
            {
                if (_reqPool.Value.Get(reqEntity).RequestId != e.RequestId) continue;
                Apply(reqEntity, e.ChosenCardEntity);
                return;
            }
        }

        void ResolveCancelled(CardPickCancelledEvent e)
        {
            if (e.RequestId == 0) return;
            foreach (var reqEntity in _reqFilter.Value)
            {
                if (_reqPool.Value.Get(reqEntity).RequestId != e.RequestId) continue;
                _world.Value.DelEntity(reqEntity);
                return;
            }
        }

        // Терминал: выбранная карта → верх колоды (index 0), остальные показанные — в конец (в исходном
        // относительном порядке). Общий для окна и авто-ролла.
        void Apply(int reqEntity, int chosenToken)
        {
            if (!_reqPool.Value.Has(reqEntity)) return;
            var r = _reqPool.Value.Get(reqEntity);   // копия — Reorder ничего не создаёт, но на всякий (единообразно с discover)

            int idx = System.Array.IndexOf(r.ShownTokens, chosenToken);
            if (idx < 0) { _world.Value.DelEntity(reqEntity); return; }   // токен не из этого предложения

            Reorder(r.OwnerPlayerEntity, r.ShownTokens, chosenToken);
            EmitResolved(r.SourceCardEntity, chosenToken);

            _world.Value.DelEntity(reqEntity);
        }

        void Reorder(int ownerPlayer, int[] shown, int chosen)
        {
            var deckPool = _deckPool.Value;
            if (!deckPool.Has(ownerPlayer)) return;
            ref var deck = ref deckPool.Get(ownerPlayer);
            if (deck.CardEntities == null) return;

            foreach (var e in shown) deck.CardEntities.Remove(e);

            deck.CardEntities.Insert(0, chosen);
            foreach (var e in shown)
                if (e != chosen) deck.CardEntities.Add(e);

            deck.Count = deck.CardEntities.Count;
        }

        // ── Не мы решаем (реплей): авто-резолв из стора; «остальные» пересчитываем ЛОКАЛЬНО (верх своей
        // колоды в момент резолва — зеркален активу, деку между офером и резолвом никто не трогает) ──
        void TryResolveRemote(int reqEntity)
        {
            if (HasEarlierPending(reqEntity)) return;
            ref var r = ref _reqPool.Value.Get(reqEntity);
            string srcKey = NetKey(r.SourceCardEntity);
            if (!CardPickReplayStore.TryPeek(srcKey, out var choice)) return;
            if (!_state.Value.TryGetEntity(choice.ChosenEntityKey, out int chosen)) return;   // ещё нет — ждём

            BuildTopOffer(ref r, out var tokens, out _, out _);
            if (tokens.Length == 0) { _world.Value.DelEntity(reqEntity); return; }

            Reorder(r.OwnerPlayerEntity, tokens, chosen);

            CardPickReplayStore.Remove(srcKey);
            _world.Value.DelEntity(reqEntity);
        }

        void EmitResolved(int sourceCard, int chosenEntity)
        {
            GameEventBus.Publish(new CardPickResolvedNetEvent
            {
                CastingCardEntity     = sourceCard,
                CastingCardNetworkKey = NetKey(sourceCard),
                ChosenCardEntity      = chosenEntity,
                ChosenCardModelId     = _modelPool.Value.Has(chosenEntity) ? _modelPool.Value.Get(chosenEntity).ModelId : -1,
                ChosenCardNetworkKey  = NetKey(chosenEntity),
                CreateFromPool        = false,
                ChosenExpansionId     = null,
                ChosenCardId          = -1,
            });
        }

        string NetKey(int entity)
            => (entity >= 0 && _netPool.Value.Has(entity)) ? _netPool.Value.Get(entity).NetworkEntityKey : null;
    }
}
