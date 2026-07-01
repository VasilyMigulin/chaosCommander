using System.Collections.Generic;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Network;
using Game.Core.Shared;
using Game.Core.Shared.Interface;
using Game.Core.Configs;
using Game.Core.Instance.Card;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Механика "раскопки" (discover) — выбор карты из предложенного пула перед кастом.
    ///
    /// Синхронизация — вилка по типу источника:
    ///   • Сессионный источник (колода/рука/кладбище) — выбрана СУЩЕСТВУЮЩАЯ у обоих
    ///     клиентов сущность; синкаем её NetworkEntityKey, оппонент находит по ключу.
    ///   • Пул (UniquePool) — карты заданы как данные; создаётся НОВАЯ сущность с общим
    ///     ключом (CreateFromPool=true), оппонент создаёт такую же через CreateCardEvent.
    ///
    /// Своя карта (OwnCardTag) → показываем UI выбора, ждём CardPickChosenEvent.
    /// Вражеская карта (реплей) → UI нет, авто-разрешаем по выбору из CardPickReplayStore.
    ///
    /// ВНИМАНИЕ: путь пула требует, чтобы CreateCardSystem была зарегистрирована в пайплайне
    /// (сейчас она не подключена) и чтобы существовали UI выбора и эффект-потребитель
    /// CardPickResultComponent — это задачи фазы контента.
    /// </summary>
    public sealed class CardPickSelectionSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsSharedInject<IGameStateContext> _state = default;

        readonly EcsFilterInject<
            Inc<CastEvent, RequiresCardPickTag, CardPickRequirementComponent>>
            _pendingPickFilter = default;

        readonly EcsPoolInject<CastEvent>                    _castPool        = default;
        readonly EcsPoolInject<RequiresCardPickTag>           _reqPickPool     = default;
        readonly EcsPoolInject<CardPickRequirementComponent>  _reqCompPool     = default;
        readonly EcsPoolInject<CardPickResultComponent>       _resultPool      = default;
        readonly EcsPoolInject<PendingCardPickComponent>      _pendingPool     = default;

        readonly EcsPoolInject<PlayerComponent>               _playerPool      = default;
        readonly EcsPoolInject<ActiveState>                   _activePool      = default;
        readonly EcsPoolInject<OwnCardTag>                    _ownCardPool     = default;
        readonly EcsPoolInject<NetworkEntityComponent>        _netKeyPool      = default;

        readonly EcsPoolInject<DeckComponent>                 _deckPool        = default;
        readonly EcsPoolInject<CardModelComponent>            _modelPool       = default;
        readonly EcsPoolInject<OwnerComponent>                _ownerPool       = default;
        readonly EcsCustomInject<CardConfig>                  _cardConfig      = default;

        readonly EcsFilterInject<Inc<HandTag,  OwnerComponent, CardModelComponent>> _handCardsFilter  = default;
        readonly EcsFilterInject<Inc<GraveTag, OwnerComponent, CardModelComponent>> _graveCardsFilter = default;

        bool _subscribed;

        readonly Queue<CardPickChosenEvent>    _chosenQueue    = new Queue<CardPickChosenEvent>();
        readonly Queue<CardPickCancelledEvent> _cancelledQueue = new Queue<CardPickCancelledEvent>();

        // Ключи карт пула, для которых уже отправлен CreateCardEvent (чтобы не дублировать)
        readonly HashSet<string> _poolCreationRequested = new HashSet<string>();

        void Subscribe()
        {
            if (_subscribed) return;
            _subscribed = true;
            GameEventBus.Subscribe<CardPickChosenEvent>(OnChosen);
            GameEventBus.Subscribe<CardPickCancelledEvent>(OnCancelled);
        }

        void OnChosen(CardPickChosenEvent e)    => _chosenQueue.Enqueue(e);
        void OnCancelled(CardPickCancelledEvent e) => _cancelledQueue.Enqueue(e);

        // ── Run ────────────────────────────────────────────────────────────────

        public void Run(IEcsSystems systems)
        {
            Subscribe();

            foreach (var cardEntity in _pendingPickFilter.Value)
            {
                if (_ownCardPool.Value.Has(cardEntity))
                    TryOfferOwn(cardEntity);
                else
                    TryResolveRemote(cardEntity);
            }

            while (_chosenQueue.Count > 0)
                ResolveChosen(_chosenQueue.Dequeue());

            while (_cancelledQueue.Count > 0)
                ResolveCancelled(_cancelledQueue.Dequeue());
        }

        // ── Своя карта: формируем предложение для UI ─────────────────────────────

        void TryOfferOwn(int cardEntity)
        {
            int playerEntity = _castPool.Value.Get(cardEntity).PlayerOwnerEntity;

            if (_pendingPool.Value.Has(playerEntity)) return; // уже предложено

            if (!_activePool.Value.Has(playerEntity)) return; // только в свой ход

            ref var req     = ref _reqCompPool.Value.Get(cardEntity);
            int[]   offered = CollectOfferedCards(ref req, playerEntity);

            ref var pending = ref _pendingPool.Value.Add(playerEntity);
            pending.CastingCardEntity   = cardEntity;
            pending.OfferedCardEntities = offered;
            pending.OfferedCount        = offered.Length;

            GameEventBus.Publish(new CardPickOfferedEvent
            {
                CastingCardEntity   = cardEntity,
                PlayerEntity        = playerEntity,
                OfferedCardEntities = offered,
                OfferedCardVisuals  = BuildVisuals(offered),
                OfferedCount        = offered.Length,
            });
        }

        CardVisualData[] BuildVisuals(int[] cardEntities)
        {
            var result = new CardVisualData[cardEntities.Length];
            for (int i = 0; i < cardEntities.Length; i++)
                result[i] = GetVisualForCard(cardEntities[i]);
            return result;
        }

        CardVisualData GetVisualForCard(int cardEntity)
        {
            if (!_modelPool.Value.Has(cardEntity)) return default;
            ref var model = ref _modelPool.Value.Get(cardEntity);
            var instance = _cardConfig.Value.Get(model.ExpansionId, model.ModelId);
            return instance?.CardData != null ? CardVisualDataFactory.From(instance.CardData) : default;
        }

        // ── Вражеская карта: авто-разрешение из стора (реплей) ───────────────────

        void TryResolveRemote(int cardEntity)
        {
            string srcKey = NetKey(cardEntity);
            if (!CardPickReplayStore.TryPeek(srcKey, out var choice)) return;

            int chosen;

            if (choice.CreateFromPool)
            {
                // Карта пула — создаём сущность (через CreateCardSystem) и ждём появления по ключу
                if (!_state.Value.TryGetEntity(choice.ChosenEntityKey, out chosen))
                {
                    if (!_poolCreationRequested.Contains(choice.ChosenEntityKey))
                    {
                        int ownerId = _ownerPool.Value.Has(cardEntity)
                            ? _ownerPool.Value.Get(cardEntity).OwnerId : -1;

                        GameEventBus.Publish(new CreateCardEvent
                        {
                            ExpansionId      = choice.ExpansionId,
                            CardId           = choice.CardId,
                            NetworkEntityKey = choice.ChosenEntityKey,
                            OwnerId          = ownerId,
                            IsEnemy          = true,
                            InHand           = false,
                        });
                        _poolCreationRequested.Add(choice.ChosenEntityKey);
                    }
                    return; // ждём пока сущность создастся
                }
            }
            else
            {
                // Сессионный источник — сущность уже есть у обоих клиентов
                if (!_state.Value.TryGetEntity(choice.ChosenEntityKey, out chosen))
                    return; // ещё не доступна — ждём
            }

            CardPickReplayStore.Remove(srcKey);
            _poolCreationRequested.Remove(choice.ChosenEntityKey);

            Finalize(cardEntity, chosen, playerEntity: -1);
        }

        // ── Resolve helpers ──────────────────────────────────────────────────────

        void ResolveChosen(in CardPickChosenEvent e)
        {
            int cardEntity = e.CastingCardEntity;
            if (!_castPool.Value.Has(cardEntity)) return;
            int playerEntity = _castPool.Value.Get(cardEntity).PlayerOwnerEntity;

            int chosen  = e.ChosenCardEntity;
            int modelId = (chosen >= 0 && _modelPool.Value.Has(chosen))
                ? _modelPool.Value.Get(chosen).ModelId : -1;

            var source = _reqCompPool.Value.Has(cardEntity)
                ? _reqCompPool.Value.Get(cardEntity).Source
                : CardPickSourceType.PlayerDeck;

            bool   isPool = source == CardPickSourceType.UniquePool;
            string chosenKey;
            string expId = null;
            int    cardId = -1;

            if (isPool)
            {
                // Пул: общий ключ для создаваемой на обоих клиентах сущности
                // ПУЛ: сущность создаётся заново на обоих клиентах → генерируем новый короткий ключ
                // (не геттер NetKey(entity)!). Полное имя — локальный метод NetKey(int) перекрывает класс.
                chosenKey = Game.Core.Network.NetKey.Next();
                if (chosen >= 0 && _modelPool.Value.Has(chosen))
                {
                    ref var m = ref _modelPool.Value.Get(chosen);
                    expId  = m.ExpansionId;
                    cardId = m.ModelId;
                }
            }
            else
            {
                // Сессионный источник: ключ существующей сущности
                chosenKey = (chosen >= 0 && _netKeyPool.Value.Has(chosen))
                    ? _netKeyPool.Value.Get(chosen).NetworkEntityKey : null;
            }

            GameEventBus.Publish(new CardPickResolvedNetEvent
            {
                CastingCardEntity     = cardEntity,
                CastingCardNetworkKey = NetKey(cardEntity),
                ChosenCardEntity      = chosen,
                ChosenCardModelId     = modelId,
                ChosenCardNetworkKey  = chosenKey,
                CreateFromPool        = isPool,
                ChosenExpansionId     = expId,
                ChosenCardId          = cardId,
            });

            Finalize(cardEntity, chosen, playerEntity);
        }

        void ResolveCancelled(in CardPickCancelledEvent e)
        {
            int cardEntity = e.CastingCardEntity;
            if (!_castPool.Value.Has(cardEntity)) return;
            int playerEntity = _castPool.Value.Get(cardEntity).PlayerOwnerEntity;

            _castPool.Value.Del(cardEntity); // каст не происходит, карта остаётся в руке

            if (_pendingPool.Value.Has(playerEntity))
                _pendingPool.Value.Del(playerEntity);
        }

        void Finalize(int cardEntity, int chosenCardEntity, int playerEntity)
        {
            if (!_resultPool.Value.Has(cardEntity))
            {
                ref var result = ref _resultPool.Value.Add(cardEntity);
                result.ChosenCardEntity  = chosenCardEntity;
                result.ChosenCardModelId = (chosenCardEntity >= 0 && _modelPool.Value.Has(chosenCardEntity))
                    ? _modelPool.Value.Get(chosenCardEntity).ModelId : -1;
            }

            if (_reqPickPool.Value.Has(cardEntity))
                _reqPickPool.Value.Del(cardEntity);

            if (playerEntity >= 0 && _pendingPool.Value.Has(playerEntity))
                _pendingPool.Value.Del(playerEntity);
        }

        // ── Сборка пула карт (локальное предложение для UI) ──────────────────────

        int[] CollectOfferedCards(ref CardPickRequirementComponent req, int playerEntity)
        {
            ref var player   = ref _playerPool.Value.Get(playerEntity);
            int     playerId = player.PlayerId;

            switch (req.Source)
            {
                case CardPickSourceType.PlayerDeck:    return TakeFromDeck(playerId, ownedByPlayer: true,  req.OfferCount);
                case CardPickSourceType.OpponentDeck:  return TakeFromDeck(playerId, ownedByPlayer: false, req.OfferCount);
                case CardPickSourceType.PlayerHand:    return TakeFromHand(playerId, ownedByPlayer: true,  req.OfferCount);
                case CardPickSourceType.OpponentHand:  return TakeFromHand(playerId, ownedByPlayer: false, req.OfferCount);
                case CardPickSourceType.PlayerGrave:   return TakeFromGrave(playerId, ownedByPlayer: true,  req.OfferCount);
                case CardPickSourceType.OpponentGrave: return TakeFromGrave(playerId, ownedByPlayer: false, req.OfferCount);
                case CardPickSourceType.UniquePool:    return TakeFromUniquePool(req.UniquePoolModelIds, req.OfferCount);
                default:                               return new int[0];
            }
        }

        int[] TakeFromDeck(int activePlayerId, bool ownedByPlayer, int count)
        {
            var result = new List<int>(count);

            foreach (var entity in _world.Value.Filter<DeckComponent>().Inc<PlayerComponent>().End())
            {
                ref var pl   = ref _playerPool.Value.Get(entity);
                bool   isOwn = pl.PlayerId == activePlayerId;
                if (isOwn != ownedByPlayer) continue;

                ref var deck = ref _deckPool.Value.Get(entity);
                for (int i = 0; i < deck.Count && result.Count < count; i++)
                    result.Add(deck.CardEntities[i]);

                break;
            }

            return result.ToArray();
        }

        int[] TakeFromHand(int activePlayerId, bool ownedByPlayer, int count)
        {
            var candidates = new List<int>();
            foreach (var ce in _handCardsFilter.Value)
            {
                bool isOwn = _ownerPool.Value.Get(ce).OwnerId == activePlayerId;
                if (isOwn == ownedByPlayer) candidates.Add(ce);
            }
            return PickRandom(candidates, count);
        }

        int[] TakeFromGrave(int activePlayerId, bool ownedByPlayer, int count)
        {
            var candidates = new List<int>();
            foreach (var ce in _graveCardsFilter.Value)
            {
                bool isOwn = _ownerPool.Value.Get(ce).OwnerId == activePlayerId;
                if (isOwn == ownedByPlayer) candidates.Add(ce);
            }
            return PickRandom(candidates, count);
        }

        int[] TakeFromUniquePool(int[] modelIds, int count)
        {
            if (modelIds == null || modelIds.Length == 0)
                return new int[0];

            var candidates = new List<int>();
            foreach (var ce in _world.Value.Filter<CardModelComponent>().End())
            {
                int modelId = _modelPool.Value.Get(ce).ModelId;
                for (int i = 0; i < modelIds.Length; i++)
                {
                    if (modelIds[i] == modelId) { candidates.Add(ce); break; }
                }
            }
            return PickRandom(candidates, count);
        }

        static int[] PickRandom(List<int> source, int count)
        {
            if (source.Count == 0) return new int[0];

            for (int i = source.Count - 1; i > 0; i--)
            {
                int j   = UnityEngine.Random.Range(0, i + 1);
                int tmp = source[i];
                source[i] = source[j];
                source[j] = tmp;
            }

            int take = System.Math.Min(count, source.Count);
            var res  = new int[take];
            for (int i = 0; i < take; i++)
                res[i] = source[i];
            return res;
        }

        string NetKey(int entity)
        {
            return (entity >= 0 && _netKeyPool.Value.Has(entity))
                ? _netKeyPool.Value.Get(entity).NetworkEntityKey : null;
        }
    }
}
