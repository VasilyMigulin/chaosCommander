using System.Collections.Generic;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Instance.Card;
using Game.Core.Mono;
using Game.Core.Service;
using Game.Core.Shared;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Пушит HP/золото/ману/активные ауры игроков из ECS в их аватары каждый кадр (паттерн
    /// CreatureStatsViewSystem — дёшево, всегда актуально, без событий на каждое изменение).
    ///   • Враг: HP+ресурсы+ауры показываются над аватаром (AvatarPlayerView.SetStats/SetAuras).
    ///   • Локальный игрок: HP идёт в ресурс-панель — публикуем ResourceChangedEvent{Health} при изменении
    ///     (над-аватарные лейблы локального скрывает сам AvatarPlayerView); ауры — AuraStatusChangedUIEvent.
    /// </summary>
    public sealed class PlayerStatsViewSystem : IEcsRunSystem
    {
        readonly EcsFilterInject<Inc<PlayerComponent, HealthComponent, AvatarViewComponent>> _filter = default;
        readonly EcsPoolInject<PlayerComponent>   _playerPool = default;
        readonly EcsPoolInject<HealthComponent>   _hpPool     = default;
        readonly EcsPoolInject<AvatarViewComponent> _avatarPool = default;
        readonly EcsPoolInject<GoldComponent>     _goldPool   = default;
        readonly EcsPoolInject<ManaComponent>     _manaPool   = default;
        readonly EcsPoolInject<HandComponent>     _handPool   = default;
        readonly EcsPoolInject<DeckComponent>     _deckPool   = default;
        readonly EcsPoolInject<OwnerComponent>    _ownerPool  = default;
        readonly EcsPoolInject<CharmTimerComponent>  _charmTimerPool = default;
        readonly EcsPoolInject<CardViewDataComponent> _viewDataPool  = default;

        int _lastLocalHp = int.MinValue, _lastLocalMaxHp = int.MinValue;
        int _lastLocalHand = int.MinValue, _lastLocalDeck = int.MinValue;
        // null (не Array.Empty) — «ещё не публиковали»: первый Run() должен опубликовать AuraStatusChangedUIEvent,
        // ДАЖЕ если аур сейчас 0 (иначе пустой Gather совпадёт с дефолтным пустым, и бар ни разу не получит
        // команду на Redraw/Clear своих слотов — см. тот же приём в AuraStatusBarView.SameAs).
        CardVisualData[] _lastLocalAuraVisuals;
        int[] _lastLocalAuraTurns;
        int[] _lastLocalAuraCounts;

        public void Run(IEcsSystems systems)
        {
            var world = systems.GetWorld();

            foreach (var pe in _filter.Value)
            {
                ref var p  = ref _playerPool.Value.Get(pe);
                ref var hp = ref _hpPool.Value.Get(pe);
                int gold = _goldPool.Value.Has(pe) ? _goldPool.Value.Get(pe).Current : 0;
                int mana = _manaPool.Value.Has(pe) ? _manaPool.Value.Get(pe).Current : 0;
                int handCount = _handPool.Value.Has(pe) ? _handPool.Value.Get(pe).Count : 0;
                int deckCount = _deckPool.Value.Has(pe) ? _deckPool.Value.Get(pe).Count : 0;

                var go = _avatarPool.Value.Get(pe).View;
                var view = go != null ? go.GetComponent<AvatarPlayerView>() : null;
                view?.SetStats(hp.Current, hp.Max, gold, mana, handCount, deckCount, p.IsLocalPlayer);

                var (auraVisuals, auraTurns, auraCounts) = GatherAuras(world, p.PlayerId);
                view?.SetAuras(auraVisuals, auraTurns, auraCounts);   // безвредно и для локального (бар скрыт вместе с _statsRoot)

                if (!p.IsLocalPlayer) continue;

                if (hp.Current != _lastLocalHp || hp.Max != _lastLocalMaxHp)
                {
                    _lastLocalHp = hp.Current;
                    _lastLocalMaxHp = hp.Max;
                    GameEventBus.Publish(new ResourceChangedEvent
                    {
                        isLocalPlayer = true,
                        Type          = EnumService.ResourceType.Health,
                        NewValue      = hp.Current,
                        MaxValue      = hp.Max,
                    });
                }

                // Рука/колода локального — в ресурс-панель (ResourceIndicatorView), как золото/мана/здоровье,
                // не над-аватарными лейблами (те AvatarPlayerView и так скрывает для локального).
                if (handCount != _lastLocalHand || deckCount != _lastLocalDeck)
                {
                    _lastLocalHand = handCount;
                    _lastLocalDeck = deckCount;
                    GameEventBus.Publish(new HandDeckCountChangedUIEvent
                    {
                        HandCount = handCount,
                        HandMax   = HandComponent.MaxHandSize,
                        DeckCount = deckCount,
                    });
                }

                // Ауры локального — в статус-бар боевой панели (AuraStatusBarView), как рука/колода: bus-событие
                // только при реальном изменении (появилась/исчезла аура, тикнул таймер), не каждый кадр.
                if (!AurasEqual(auraVisuals, auraTurns, auraCounts, _lastLocalAuraVisuals, _lastLocalAuraTurns, _lastLocalAuraCounts))
                {
                    _lastLocalAuraVisuals = auraVisuals;
                    _lastLocalAuraTurns = auraTurns;
                    _lastLocalAuraCounts = auraCounts;
                    GameEventBus.Publish(new AuraStatusChangedUIEvent
                    {
                        Visuals        = auraVisuals,
                        TurnsRemaining = auraTurns,
                        StackCounts    = auraCounts,
                    });
                }
            }
        }

        /// <summary>Активные ауры (чары на поле, БЕЗ токенов) владельца ownerId, СГРУППИРОВАННЫЕ по имени
        /// (одинаковые чары — один слот со стаком ×N; Мистер Постоянство может дать больше слотов бара).
        /// TurnsRemaining[i] — БЛИЖАЙШИЙ к истечению таймер группы (-1 — все копии постоянные);
        /// StackCounts[i] — размер стака. Порядок: «скоро истекут — первыми», постоянные в конце —
        /// при переполнении бара (слотов меньше, чем групп) показывается самое срочное.</summary>
        (CardVisualData[], int[], int[]) GatherAuras(EcsWorld world, int ownerId)
        {
            var groups = new List<(CardVisualData visual, int turns, int count)>();

            foreach (var e in world.Filter<CharmTag>().Inc<BoardTag>().Inc<OwnerComponent>().Exc<TokenTag>().End())
            {
                if (_ownerPool.Value.Get(e).OwnerId != ownerId) continue;
                if (!_viewDataPool.Value.Has(e)) continue;

                int turnsRemaining = _charmTimerPool.Value.Has(e) ? _charmTimerPool.Value.Get(e).TurnsRemaining : -1;
                var visual = CardVisualDataFactory.From(in _viewDataPool.Value.Get(e));

                int idx = groups.FindIndex(g => g.visual.CardName == visual.CardName);
                if (idx < 0) { groups.Add((visual, turnsRemaining, 1)); continue; }
                var g = groups[idx];
                g.count++;
                if (SortKey(turnsRemaining) < SortKey(g.turns)) g.turns = turnsRemaining;
                groups[idx] = g;
            }

            groups.Sort((a, b) => SortKey(a.turns).CompareTo(SortKey(b.turns)));

            var visuals = new CardVisualData[groups.Count];
            var turns   = new int[groups.Count];
            var counts  = new int[groups.Count];
            for (int i = 0; i < groups.Count; i++)
            {
                visuals[i] = groups[i].visual;
                turns[i]   = groups[i].turns;
                counts[i]  = groups[i].count;
            }
            return (visuals, turns, counts);
        }

        // Ключ сортировки/сравнения таймеров: постоянная чара (-1) — «истекает позже всех».
        static int SortKey(int turns) => turns < 0 ? int.MaxValue : turns;

        static bool AurasEqual(CardVisualData[] visualsA, int[] turnsA, int[] countsA,
                               CardVisualData[] visualsB, int[] turnsB, int[] countsB)
        {
            if (visualsB == null) return false;   // ещё не публиковали — форсим первую публикацию
            if (visualsA.Length != visualsB.Length) return false;
            for (int i = 0; i < visualsA.Length; i++)
                if (turnsA[i] != turnsB[i] || countsA[i] != countsB[i] || visualsA[i].CardName != visualsB[i].CardName) return false;
            return true;
        }
    }
}
