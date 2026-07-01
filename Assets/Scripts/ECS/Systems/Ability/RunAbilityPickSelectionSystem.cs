using System;
using System.Collections.Generic;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Configs;
using Game.Core.Instance.Card;
using Game.Core.Shared;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// Async-выбор цели для Target/Selected из НЕ-Board зон (колода/рука/кладбище) — через окно выбора
    /// (CardPickOfferedEvent → PickupWindow → CardPickChosenEvent), а не клики по доске. Гейзер кандидатов —
    /// TargetGather по зоне/фильтрам способности. Набрав Count → AbilityQueuedState{Targets}.
    ///
    /// СИНК: окно показывается только на активе (AbilityPickPendingState ставится в таргетинге, который идёт
    /// только у активного). Выбранная цель уходит в Targets → ActionAbilityData.TargetEntityKeys, пассив
    /// берёт её из снапшота (не переспрашивает). Спец-канал не нужен.
    /// </summary>
    public sealed class RunAbilityPickSelectionSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsCustomInject<CardConfig> _cardConfig = default;

        readonly EcsFilterInject<Inc<AbilityPickPendingState, AbilityTargetComponent, AbilityOwnerComponent>> _pendingFilter = default;
        readonly EcsPoolInject<AbilityPickPendingState> _pickPool   = default;
        readonly EcsPoolInject<AbilityTargetComponent>  _targetPool = default;
        readonly EcsPoolInject<AbilityOwnerComponent>   _ownerPool  = default;
        readonly EcsPoolInject<AbilityQueuedState>      _queuedPool = default;
        readonly EcsPoolInject<ActiveState>             _activePool = default;
        readonly EcsPoolInject<CardModelComponent>      _modelPool  = default;

        bool _subscribed;
        readonly Queue<CardPickChosenEvent>    _chosen    = new Queue<CardPickChosenEvent>();
        readonly Queue<CardPickCancelledEvent> _cancelled = new Queue<CardPickCancelledEvent>();

        void Subscribe()
        {
            if (_subscribed) return;
            _subscribed = true;
            GameEventBus.Subscribe<CardPickChosenEvent>(e => _chosen.Enqueue(e));
            GameEventBus.Subscribe<CardPickCancelledEvent>(e => _cancelled.Enqueue(e));
        }

        public void Run(IEcsSystems systems)
        {
            Subscribe();

            // Предложить ещё не показанные пики (только в свой ход).
            foreach (var ability in _pendingFilter.Value)
            {
                ref var pp = ref _pickPool.Value.Get(ability);
                if (pp.Offered) continue;
                if (!_activePool.Value.Has(pp.PlayerEntity)) continue;

                ref var owner = ref _ownerPool.Value.Get(ability);
                ref var tc    = ref _targetPool.Value.Get(ability);

                var candidates = TargetGather.Gather(_world.Value, tc.Filters, owner.CardEntity, owner.PlayerEntity, null, tc.Zone);
                if (candidates.Count == 0) { _pickPool.Value.Del(ability); continue; }   // фуззл

                // discover: показываем ОГРАНИЧЕННЫЙ случайный набор (OfferCount), а не все кандидаты —
                // окно вмещает немного карт. Синкается только финальный выбор (через target-ключ).
                var shown = TakeRandom(candidates, tc.OfferCount);

                pp.Offered = true;
                GameEventBus.Publish(new CardPickOfferedEvent
                {
                    CastingCardEntity   = owner.CardEntity,   // корреляция (эхо в CardPickChosenEvent)
                    PlayerEntity        = owner.PlayerEntity,
                    OfferedCardEntities = shown.ToArray(),
                    OfferedCardVisuals  = BuildVisuals(shown),
                    OfferedCount        = shown.Count,
                });
            }

            while (_chosen.Count > 0)    ResolveChosen(_chosen.Dequeue());
            while (_cancelled.Count > 0) ResolveCancelled(_cancelled.Dequeue());
        }

        void ResolveChosen(CardPickChosenEvent e)
        {
            foreach (var ability in _pendingFilter.Value)
            {
                if (_ownerPool.Value.Get(ability).CardEntity != e.CastingCardEntity) continue;

                ref var pp = ref _pickPool.Value.Get(ability);
                ref var tc = ref _targetPool.Value.Get(ability);

                var chosen = pp.Chosen ?? Array.Empty<int>();
                foreach (var c in chosen) if (c == e.ChosenCardEntity) return;   // не дублируем

                var next = new int[chosen.Length + 1];
                Array.Copy(chosen, next, chosen.Length);
                next[chosen.Length] = e.ChosenCardEntity;
                pp.Chosen = next;

                if (next.Length >= tc.Count)
                {
                    if (!_queuedPool.Value.Has(ability)) _queuedPool.Value.Add(ability);
                    _queuedPool.Value.Get(ability).Targets = next;
                    _pickPool.Value.Del(ability);
                }
                return;
            }
        }

        void ResolveCancelled(CardPickCancelledEvent e)
        {
            foreach (var ability in _pendingFilter.Value)
            {
                if (_ownerPool.Value.Get(ability).CardEntity != e.CastingCardEntity) continue;
                _pickPool.Value.Del(ability);   // отмена → способность не разыграется
                return;
            }
        }

        // discover: случайный поднабор размера n (n<=0 или >=всего → все). Только UI на активе → не синкаем.
        static List<int> TakeRandom(List<int> src, int n)
        {
            if (n <= 0 || src.Count <= n) return src;
            for (int i = 0; i < n; i++)
            {
                int j = UnityEngine.Random.Range(i, src.Count);
                (src[i], src[j]) = (src[j], src[i]);
            }
            var res = new List<int>(n);
            for (int i = 0; i < n; i++) res.Add(src[i]);
            return res;
        }

        CardVisualData[] BuildVisuals(List<int> cards)
        {
            var res = new CardVisualData[cards.Count];
            for (int i = 0; i < cards.Count; i++)
            {
                if (!_modelPool.Value.Has(cards[i])) continue;
                ref var m = ref _modelPool.Value.Get(cards[i]);
                var inst = _cardConfig.Value.Get(m.ExpansionId, m.ModelId);
                if (inst?.CardData != null) res[i] = CardVisualDataFactory.From(inst.CardData);
            }
            return res;
        }
    }
}
