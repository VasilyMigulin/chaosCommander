using System.Collections.Generic;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Управляет механикой "раскопки" (discover) — выбором карты из предложенного пула
    /// перед тем как CastCardSystem обработает каст.
    ///
    /// Пайплайн одного фрейма:
    ///   1. На карте есть CastEvent + RequiresCardPickTag + CardPickRequirementComponent.
    ///   2. Система собирает пул карт согласно Source, формирует PendingCardPickComponent
    ///      на entity игрока и публикует CardPickOfferedEvent → UI показывает панель.
    ///   3. UI получает CardPickChosenEvent (пользователь кликнул) или
    ///      CardPickCancelledEvent (пользователь закрыл).
    ///      — При выборе: пишем CardPickResultComponent на карту, снимаем
    ///        RequiresCardPickTag → в следующем фрейме CastCardSystem обработает каст.
    ///      — При отмене: снимаем CastEvent, снимаем PendingCardPickComponent.
    ///        Карта остаётся в руке.
    ///   4. После каста CardPickResultComponent читается эффектами способности.
    ///      Удаляется в конце резолва (через DelHere в EcsRunHandler).
    /// </summary>
    public sealed class CardPickSelectionSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;

        // ── Фильтры ────────────────────────────────────────────────────────────

        /// Карты ожидающие pick: есть CastEvent + RequiresCardPickTag
        readonly EcsFilterInject<
            Inc<CastEvent, RequiresCardPickTag, CardPickRequirementComponent>>
            _pendingPickFilter = default;

        // ── Пулы ───────────────────────────────────────────────────────────────

        readonly EcsPoolInject<CastEvent>                    _castPool        = default;
        readonly EcsPoolInject<RequiresCardPickTag>           _reqPickPool     = default;
        readonly EcsPoolInject<CardPickRequirementComponent>  _reqCompPool     = default;
        readonly EcsPoolInject<CardPickResultComponent>       _resultPool      = default;
        readonly EcsPoolInject<PendingCardPickComponent>      _pendingPool     = default;

        readonly EcsPoolInject<PlayerComponent>               _playerPool      = default;
        readonly EcsPoolInject<TurnPhaseState>                _phasePool       = default;

        // Источники карт
        readonly EcsPoolInject<DeckComponent>                 _deckPool        = default;
        readonly EcsPoolInject<CardModelComponent>            _modelPool       = default;
        readonly EcsPoolInject<OwnerComponent>                _ownerPool       = default;

        // Фильтры карт по локации
        readonly EcsFilterInject<Inc<HandTag,  OwnerComponent, CardModelComponent>> _handCardsFilter  = default;
        readonly EcsFilterInject<Inc<GraveTag, OwnerComponent, CardModelComponent>> _graveCardsFilter = default;

        // ── Подписка на события UI ─────────────────────────────────────────────

        bool _subscribed;

        // Очередь событий от UI (пишутся в коллбэках, читаются в Run)
        readonly Queue<CardPickChosenEvent>    _chosenQueue    = new Queue<CardPickChosenEvent>();
        readonly Queue<CardPickCancelledEvent> _cancelledQueue = new Queue<CardPickCancelledEvent>();

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

            // 1. Обрабатываем новые карты требующие pick (инициируем pending)
            foreach (var cardEntity in _pendingPickFilter.Value)
            {
                ref var castEvent = ref _castPool.Value.Get(cardEntity);
                int playerEntity  = castEvent.OwnerEntity;

                // Если pending уже создан — пропускаем
                if (_pendingPool.Value.Has(playerEntity)) continue;

                // Только во время хода игрока
                if (!_phasePool.Value.Has(playerEntity)) continue;
                ref var phase = ref _phasePool.Value.Get(playerEntity);
                if (phase.Phase != TurnPhase.PlayerTurn) continue;

                ref var req      = ref _reqCompPool.Value.Get(cardEntity);
                int[]   offered  = CollectOfferedCards(ref req, playerEntity);
                int     offCount = offered.Length;

                // Создаём pending на игроке
                ref var pending = ref _pendingPool.Value.Add(playerEntity);
                pending.CastingCardEntity  = cardEntity;
                pending.OfferedCardEntities = offered;
                pending.OfferedCount        = offCount;

                // Сообщаем UI
                GameEventBus.Publish(new CardPickOfferedEvent
                {
                    CastingCardEntity   = cardEntity,
                    PlayerEntity        = playerEntity,
                    OfferedCardEntities = offered,
                    OfferedCount        = offCount,
                });
            }

            // 2. Обрабатываем ответ UI — выбор карты
            while (_chosenQueue.Count > 0)
            {
                var chosen = _chosenQueue.Dequeue();
                ResolveChosen(chosen);
            }

            // 3. Обрабатываем отмену
            while (_cancelledQueue.Count > 0)
            {
                var cancelled = _cancelledQueue.Dequeue();
                ResolveCancelled(cancelled);
            }
        }

        // ── Resolve helpers ────────────────────────────────────────────────────

        void ResolveChosen(in CardPickChosenEvent e)
        {
            int cardEntity = e.CastingCardEntity;

            // Находим player-entity через CastEvent
            if (!_castPool.Value.Has(cardEntity)) return;
            int playerEntity = _castPool.Value.Get(cardEntity).OwnerEntity;

            // Пишем результат на карту
            if (!_resultPool.Value.Has(cardEntity))
            {
                ref var result = ref _resultPool.Value.Add(cardEntity);
                result.ChosenCardEntity = e.ChosenCardEntity;
                result.ChosenCardModelId = _modelPool.Value.Has(e.ChosenCardEntity)
                    ? _modelPool.Value.Get(e.ChosenCardEntity).ModelId
                    : -1;

                // Сетевая репликация выбора
                GameEventBus.Publish(new CardPickResolvedNetEvent
                {
                    CastingCardEntity = cardEntity,
                    ChosenCardEntity  = e.ChosenCardEntity,
                    ChosenCardModelId = result.ChosenCardModelId,
                });
            }

            // Снимаем тег — в следующем фрейме CastCardSystem обработает каст
            if (_reqPickPool.Value.Has(cardEntity))
                _reqPickPool.Value.Del(cardEntity);

            // Снимаем pending
            if (_pendingPool.Value.Has(playerEntity))
                _pendingPool.Value.Del(playerEntity);
        }

        void ResolveCancelled(in CardPickCancelledEvent e)
        {
            int cardEntity = e.CastingCardEntity;

            if (!_castPool.Value.Has(cardEntity)) return;
            int playerEntity = _castPool.Value.Get(cardEntity).OwnerEntity;

            // Снимаем CastEvent — карта остаётся в руке, каст не происходит
            _castPool.Value.Del(cardEntity);

            if (_pendingPool.Value.Has(playerEntity))
                _pendingPool.Value.Del(playerEntity);
        }

        // ── Сборка пула карт ───────────────────────────────────────────────────

        int[] CollectOfferedCards(ref CardPickRequirementComponent req, int playerEntity)
        {
            ref var player   = ref _playerPool.Value.Get(playerEntity);
            int     playerId = player.PlayerId;

            switch (req.Source)
            {
                case CardPickSourceType.PlayerDeck:
                    return TakeFromDeck(playerId, ownedByPlayer: true, req.OfferCount);

                case CardPickSourceType.OpponentDeck:
                    return TakeFromDeck(playerId, ownedByPlayer: false, req.OfferCount);

                case CardPickSourceType.PlayerHand:
                    return TakeFromHand(playerId, ownedByPlayer: true, req.OfferCount);

                case CardPickSourceType.OpponentHand:
                    return TakeFromHand(playerId, ownedByPlayer: false, req.OfferCount);

                case CardPickSourceType.PlayerGrave:
                    return TakeFromGrave(playerId, ownedByPlayer: true, req.OfferCount);

                case CardPickSourceType.OpponentGrave:
                    return TakeFromGrave(playerId, ownedByPlayer: false, req.OfferCount);

                case CardPickSourceType.UniquePool:
                    return TakeFromUniquePool(req.UniquePoolModelIds, req.OfferCount);

                default:
                    return new int[0];
            }
        }

        /// Верхние OfferCount карт из ECS-колоды игрока/оппонента.
        /// DeckComponent хранится на entity игрока, поэтому ищем через PlayerComponent.
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

        /// Случайные OfferCount карт из руки игрока/оппонента через HandTag
        int[] TakeFromHand(int activePlayerId, bool ownedByPlayer, int count)
        {
            var candidates = new List<int>();

            foreach (var ce in _handCardsFilter.Value)
            {
                int ownerId = _ownerPool.Value.Get(ce).OwnerId;
                bool isOwn  = ownerId == activePlayerId;
                if (isOwn == ownedByPlayer)
                    candidates.Add(ce);
            }

            return PickRandom(candidates, count);
        }

        /// Случайные OfferCount карт с кладбища игрока/оппонента
        int[] TakeFromGrave(int activePlayerId, bool ownedByPlayer, int count)
        {
            var candidates = new List<int>();

            foreach (var ce in _graveCardsFilter.Value)
            {
                int ownerId = _ownerPool.Value.Get(ce).OwnerId;
                bool isOwn  = ownerId == activePlayerId;
                if (isOwn == ownedByPlayer)
                    candidates.Add(ce);
            }

            return PickRandom(candidates, count);
        }

        /// Берёт count случайных карт из уникального пула по ModelId
        int[] TakeFromUniquePool(int[] modelIds, int count)
        {
            if (modelIds == null || modelIds.Length == 0)
                return new int[0];

            // Создаём виртуальные entity из пула по ModelId
            // Ищем существующие entity с подходящим ModelId среди всех карт
            var candidates = new List<int>();
            foreach (var ce in _world.Value.Filter<CardModelComponent>().End())
            {
                int modelId = _modelPool.Value.Get(ce).ModelId;
                for (int i = 0; i < modelIds.Length; i++)
                {
                    if (modelIds[i] == modelId)
                    {
                        candidates.Add(ce);
                        break;
                    }
                }
            }

            return PickRandom(candidates, count);
        }

        // ── Утилиты ───────────────────────────────────────────────────────────

        static int[] PickRandom(List<int> source, int count)
        {
            if (source.Count == 0) return new int[0];

            // Перемешиваем Fisher-Yates
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
    }
}
