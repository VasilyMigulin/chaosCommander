using System;
using System.Collections.Generic;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Network;
using Game.Core.Configs;
using Game.Core.Shared.Interface;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Применяет PickCardEffectComponent (мид-цепочечная раскопка):
    ///   • Свои карты — публикует CardPickOfferedEvent (UI выбора), ждёт CardPickChosenEvent,
    ///     пишет выбранную сущность в ChainStateComponent.ProducedEntity, публикует
    ///     CardPickResolvedNetEvent (CollectActionSystem подхватит и отправит снэпшот),
    ///     удаляет effect-entity.
    ///   • Вражеские (replay) — ждёт запись в CardPickReplayStore (приходит через
    ///     ActionCardPickedData). Для пула публикует CreateCardEvent и ждёт создания.
    ///     После — удаляет effect-entity (целевая сущность уже доступна по ключу).
    /// </summary>
    public sealed class ApplyPickCardSystem : IEcsInitSystem, IEcsRunSystem, IDisposable
    {
        readonly EcsWorldInject _world = default;
        readonly EcsSharedInject<IGameStateContext> _state = default;
        readonly EcsCustomInject<CardConfig> _cardConfig = default;

        readonly EcsFilterInject<Inc<HitComponent, PickCardEffectComponent, EffectAbilityRefComponent>> _filter = default;

        readonly EcsPoolInject<PickCardEffectComponent> _pickEffPool = default;
        readonly EcsPoolInject<EffectAbilityRefComponent> _refPool = default;
        readonly EcsPoolInject<PickCardInFlightTag> _inFlightPool = default;
        readonly EcsPoolInject<ChainStateComponent> _chainStatePool = default;
        readonly EcsPoolInject<AbilitySourceComponent> _abilitySourcePool = default;
        readonly EcsPoolInject<NetworkEntityComponent> _netKeyPool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;
        readonly EcsPoolInject<OwnCardTag> _ownCardPool = default;
        readonly EcsPoolInject<CardModelComponent> _modelPool = default;

        readonly EcsFilterInject<Inc<PlayerComponent>> _playerFilter = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;

        readonly Queue<CardPickChosenEvent> _chosenQueue = new Queue<CardPickChosenEvent>();
        readonly HashSet<string> _poolCreationRequested = new HashSet<string>();
        bool _subscribed;

        public void Init(IEcsSystems systems) => Subscribe();

        public void Dispose()
        {
            if (!_subscribed) return;
            GameEventBus.Unsubscribe<CardPickChosenEvent>(OnChosen);
            _subscribed = false;
        }

        void Subscribe()
        {
            if (_subscribed) return;
            _subscribed = true;
            GameEventBus.Subscribe<CardPickChosenEvent>(OnChosen);
        }

        void OnChosen(CardPickChosenEvent e) => _chosenQueue.Enqueue(e);

        public void Run(IEcsSystems systems)
        {
            Subscribe();

            // 1) Сначала прокручиваем очередь выборов — это удалит resolved effect-entity,
            //    чтобы они не попали под повторный Offer.
            while (_chosenQueue.Count > 0)
                ResolveOwn(_chosenQueue.Dequeue());

            // 2) Для оставшихся PickCard effect-entity: Offer (свои) или Replay (враги).
            foreach (var effectEntity in _filter.Value)
            {
                ref var refComp = ref _refPool.Value.Get(effectEntity);
                int abilityEntity = refComp.AbilityEntity;
                int sourceCard = GetSourceCard(abilityEntity);
                if (sourceCard < 0)
                {
                    _world.Value.DelEntity(effectEntity);
                    continue;
                }

                bool isOwn = _ownCardPool.Value.Has(sourceCard);
                if (isOwn)
                {
                    if (!_inFlightPool.Value.Has(effectEntity))
                        Offer(effectEntity, sourceCard);
                }
                else
                {
                    TryReplayOnEnemy(effectEntity, sourceCard);
                }
            }
        }

        // ── Свои: предложение карт UI ────────────────────────────────────────
        void Offer(int effectEntity, int sourceCard)
        {
            int playerEntity = FindPlayerEntityOf(sourceCard);
            if (playerEntity < 0) return; // владелец не найден — повторим позже

            ref var eff = ref _pickEffPool.Value.Get(effectEntity);
            int[] offered = CardPickHelper.CollectOffered(_world.Value, eff.Source,
                eff.OfferCount, eff.UniquePoolModelIds, playerEntity);
            var visuals = CardPickHelper.BuildVisuals(_world.Value, _cardConfig.Value, offered);

            GameEventBus.Publish(new CardPickOfferedEvent
            {
                CastingCardEntity   = sourceCard,
                PlayerEntity        = playerEntity,
                OfferedCardEntities = offered,
                OfferedCardVisuals  = visuals,
                OfferedCount        = offered.Length,
            });

            _inFlightPool.Value.Add(effectEntity);
        }

        // ── Резолюция выбора игрока ──────────────────────────────────────────
        void ResolveOwn(in CardPickChosenEvent evt)
        {
            foreach (var effectEntity in _filter.Value)
            {
                if (!_inFlightPool.Value.Has(effectEntity)) continue;

                ref var refComp = ref _refPool.Value.Get(effectEntity);
                int abilityEntity = refComp.AbilityEntity;
                int sourceCard = GetSourceCard(abilityEntity);
                if (sourceCard != evt.CastingCardEntity) continue;

                int chosen = evt.ChosenCardEntity;

                ref var eff = ref _pickEffPool.Value.Get(effectEntity);
                bool isPool = eff.Source == CardPickSourceType.UniquePool;

                string chosenKey;
                string expId = null;
                int cardId = -1;

                if (isPool)
                {
                    chosenKey = Guid.NewGuid().ToString();
                    if (chosen >= 0 && _modelPool.Value.Has(chosen))
                    {
                        ref var m = ref _modelPool.Value.Get(chosen);
                        expId  = m.ExpansionId;
                        cardId = m.ModelId;
                    }
                }
                else
                {
                    chosenKey = NetKey(chosen);
                }

                int chosenModelId = (chosen >= 0 && _modelPool.Value.Has(chosen))
                    ? _modelPool.Value.Get(chosen).ModelId : -1;

                GameEventBus.Publish(new CardPickResolvedNetEvent
                {
                    CastingCardEntity     = sourceCard,
                    CastingCardNetworkKey = NetKey(sourceCard),
                    ChosenCardEntity      = chosen,
                    ChosenCardModelId     = chosenModelId,
                    ChosenCardNetworkKey  = chosenKey,
                    CreateFromPool        = isPool,
                    ChosenExpansionId     = expId,
                    ChosenCardId          = cardId,
                });

                // Результат шага → следующий шаг с PreviousProduced получит выбранную карту.
                if (_chainStatePool.Value.Has(abilityEntity))
                {
                    ref var state = ref _chainStatePool.Value.Get(abilityEntity);
                    state.ProducedEntity = chosen;
                }

                _world.Value.DelEntity(effectEntity);
                return;
            }
        }

        // ── Враги: ждём CardPickReplayStore (заполняется ActionCardPickedData) ──
        void TryReplayOnEnemy(int effectEntity, int sourceCard)
        {
            string srcKey = NetKey(sourceCard);
            if (string.IsNullOrEmpty(srcKey)) return;
            if (!CardPickReplayStore.TryPeek(srcKey, out var choice)) return;

            int chosen;
            if (choice.CreateFromPool)
            {
                if (!_state.Value.TryGetEntity(choice.ChosenEntityKey, out chosen))
                {
                    if (!_poolCreationRequested.Contains(choice.ChosenEntityKey))
                    {
                        int ownerId = _ownerPool.Value.Has(sourceCard)
                            ? _ownerPool.Value.Get(sourceCard).OwnerId : -1;

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
                    return;
                }
            }
            else
            {
                if (!_state.Value.TryGetEntity(choice.ChosenEntityKey, out chosen))
                    return;
            }

            CardPickReplayStore.Remove(srcKey);
            _poolCreationRequested.Remove(choice.ChosenEntityKey);
            _world.Value.DelEntity(effectEntity);
        }

        int GetSourceCard(int abilityEntity)
        {
            if (!_abilitySourcePool.Value.Has(abilityEntity)) return -1;
            return _abilitySourcePool.Value.Get(abilityEntity).CardEntity;
        }

        int FindPlayerEntityOf(int sourceCard)
        {
            if (!_ownerPool.Value.Has(sourceCard)) return -1;
            int ownerId = _ownerPool.Value.Get(sourceCard).OwnerId;
            foreach (var pe in _playerFilter.Value)
                if (_playerPool.Value.Get(pe).PlayerId == ownerId) return pe;
            return -1;
        }

        string NetKey(int entity)
        {
            if (entity < 0 || !_netKeyPool.Value.Has(entity)) return null;
            return _netKeyPool.Value.Get(entity).NetworkEntityKey;
        }
    }
}
