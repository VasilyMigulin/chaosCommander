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
    /// Окно — ОБЩИЙ ресурс на четыре канала пика, поэтому оффер публикуется только со слотом от
    /// CardPickBrokerSystem, а выбор коррелируется по RequestId (не по entity карты-владельца: шину
    /// слушают все каналы, а id живут в одном пространстве).
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
        readonly EcsPoolInject<PlayerComponent>         _playerPool = default;

        bool _subscribed;
        readonly Queue<CardPickChosenEvent>    _chosen    = new Queue<CardPickChosenEvent>();
        readonly Queue<CardPickCancelledEvent> _cancelled = new Queue<CardPickCancelledEvent>();
        readonly Queue<int>                    _expired   = new Queue<int>();
        readonly List<int>                     _buffer    = new List<int>();

        void Subscribe()
        {
            if (_subscribed) return;
            _subscribed = true;
            GameEventBus.Subscribe<CardPickChosenEvent>(e => _chosen.Enqueue(e));
            GameEventBus.Subscribe<CardPickCancelledEvent>(e => _cancelled.Enqueue(e));
            // Момент истечения задаёт брокер (одно правило на все каналы пика); способ наш — отмена:
            // недобранные цели означают, что способность не разыгрывается.
            GameEventBus.Subscribe<CardPickExpiredEvent>(e => _expired.Enqueue(e.PlayerId));
        }

        public void Run(IEcsSystems systems)
        {
            Subscribe();

            Offer();

            while (_chosen.Count > 0)    ResolveChosen(_chosen.Dequeue());
            while (_cancelled.Count > 0) ResolveCancelled(_cancelled.Dequeue());
            while (_expired.Count > 0)   Expire(_expired.Dequeue());
        }

        // Предложить ещё не показанные пики (только в свой ход и только со слотом окна).
        void Offer()
        {
            _buffer.Clear();
            foreach (var ability in _pendingFilter.Value) _buffer.Add(ability);

            foreach (var ability in _buffer)
            {
                if (!_pickPool.Value.Has(ability)) continue;
                ref var pp = ref _pickPool.Value.Get(ability);
                if (pp.Offered) continue;
                if (!_activePool.Value.Has(pp.PlayerEntity)) continue;

                ref var owner = ref _ownerPool.Value.Get(ability);
                ref var tc    = ref _targetPool.Value.Get(ability);

                var candidates = TargetGather.Gather(_world.Value, tc.Filters, owner.CardEntity, owner.PlayerEntity, null, tc.Zone);
                Exclude(candidates, pp.Chosen);            // уже выбранные повторно не показываем
                if (candidates.Count == 0) { Release(ability); continue; }   // фуззл

                if (!PickTicket.Ready(_world.Value, ability, ref pp.RequestId, pp.PlayerEntity)) continue;

                // discover: показываем ОГРАНИЧЕННЫЙ случайный набор (OfferCount), а не все кандидаты —
                // окно вмещает немного карт. Синкается только финальный выбор (через target-ключ).
                var shown = TakeRandom(candidates, tc.OfferCount);

                pp.Offered = true;
                GameEventBus.Publish(new CardPickOfferedEvent
                {
                    RequestId           = pp.RequestId,
                    CastingCardEntity   = owner.CardEntity,   // диагностика; корреляция — только по RequestId
                    PlayerEntity        = owner.PlayerEntity,
                    OfferedCardEntities = shown.ToArray(),
                    OfferedCardVisuals  = BuildVisuals(shown),
                    OfferedCount        = shown.Count,
                    AllowCancel         = true,
                });
            }
        }

        void ResolveChosen(CardPickChosenEvent e)
        {
            if (e.RequestId == 0) return;

            foreach (var ability in _pendingFilter.Value)
            {
                ref var pp = ref _pickPool.Value.Get(ability);
                if (pp.RequestId != e.RequestId) continue;

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
                    Release(ability);
                    return;
                }

                // Нужно ещё целей (Count>1): отпускаем окно и встаём в очередь брокера заново — иначе
                // после первого выбора запрос оставался с Offered=true и второе окно не показывалось
                // никогда, а способность зависала до конца хода.
                pp.Offered   = false;
                pp.RequestId = 0;
                PickTicket.Release(_world.Value, ability);
                return;
            }
        }

        void ResolveCancelled(CardPickCancelledEvent e)
        {
            if (e.RequestId == 0) return;
            foreach (var ability in _pendingFilter.Value)
            {
                if (_pickPool.Value.Get(ability).RequestId != e.RequestId) continue;
                Release(ability);   // отмена → способность не разыграется
                return;
            }
        }

        // Ход закончился, а окно выбора (колода/рука/кладбище) ещё открыто — не должно зависать после
        // EndTurn. Отменяем (способность не резолвится) — см. тот же комментарий в
        // RunAbilityTargetSelectionSystem про OnCast-only и невозвращённую карту (TODO).
        void Expire(int playerId)
        {
            _buffer.Clear();
            foreach (var ability in _pendingFilter.Value)
            {
                int pe = _pickPool.Value.Get(ability).PlayerEntity;
                if (_playerPool.Value.Has(pe) && _playerPool.Value.Get(pe).PlayerId == playerId) _buffer.Add(ability);
            }

            foreach (var ability in _buffer) Release(ability);
        }

        // Снять запрос вместе с талоном окна: сущность способности переживает пик, сам талон не исчезнет.
        void Release(int ability)
        {
            if (_pickPool.Value.Has(ability)) _pickPool.Value.Del(ability);
            PickTicket.Release(_world.Value, ability);
        }

        static void Exclude(List<int> src, int[] taken)
        {
            if (taken == null || taken.Length == 0) return;
            for (int i = src.Count - 1; i >= 0; i--)
                for (int j = 0; j < taken.Length; j++)
                    if (src[i] == taken[j]) { src.RemoveAt(i); break; }
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
