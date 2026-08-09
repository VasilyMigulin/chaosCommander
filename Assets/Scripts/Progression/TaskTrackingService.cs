using System.Collections.Generic;
using Game.Core.Backend;   // DailyService
using Game.Core.Events;
using UnityEngine;

namespace Game.Core.Progression
{
    /// <summary>
    /// Точка сборки трекинга задач. Ставится один раз на старте приложения (RuntimeInitializeOnLoadMethod):
    /// регистрирует все трекеры (персистентные подписки на шину) и держит локальный PlayerId. Прогресс копится
    /// в TaskProgress и уходит на сервер ОДНИМ батч-вызовом в конце матча (без спама и гонок за журнал).
    ///
    /// open_boosters тут НЕ трекается — его инкрементит сервер в OpenBooster.
    /// Четыре типа (summon_creatures / fill_board / charm_turns / deathrattle) ждут новых событий из ECS.
    /// </summary>
    public static class TaskTrackingService
    {
        static readonly List<TaskTracker> _trackers = new();
        static bool _installed;

        /// <summary>PlayerId локального игрока (из PlayerAssignedEvent). -1 = вне матча. Нужен трекерам,
        /// отделяющим свои действия от чужих (play_cards/kill/damage/mana считают только своё).</summary>
        public static int LocalPlayerId { get; private set; } = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void Install()
        {
            if (_installed) return;
            _installed = true;

            // локальный игрок — для фильтра «своё/чужое» в трекерах
            GameEventBus.SubscribePersistent<PlayerAssignedEvent>(OnPlayerAssigned);

            _trackers.Add(new PlayCardsTracker());
            _trackers.Add(new KillCreaturesTracker());
            _trackers.Add(new DealDamageTracker());
            _trackers.Add(new WinGamesTracker());
            _trackers.Add(new PlayGamesTracker());
            _trackers.Add(new SpendManaTracker());
            _trackers.Add(new RestoreManaTracker());
            // хуки из ECS (добавлены публикации в системах):
            _trackers.Add(new SummonCreaturesTracker());
            _trackers.Add(new FillBoardTracker());
            _trackers.Add(new CharmTurnsTracker());
            _trackers.Add(new DeathrattleTracker());
            foreach (var t in _trackers) t.Subscribe();

            // ВАЖНО: флеш подписан ПОСЛЕ трекеров → их MatchEnded-обработчики (win/play games) успевают записать
            // прогресс до отправки. Один батч-вызов на матч.
            GameEventBus.SubscribePersistent<MatchEndedEvent>(OnMatchEnded);

            Debug.Log($"[TaskTracking] installed ({_trackers.Count} trackers)");
        }

        static void OnPlayerAssigned(PlayerAssignedEvent e)
        {
            if (e.IsLocalPlayer) LocalPlayerId = e.PlayerId;
        }

        static void OnMatchEnded(MatchEndedEvent _)
        {
            var pending = TaskProgress.TakeAll();

            // UI (MatchRewardsWindow, секция «задачи за матч») получает дельты этого матча — публикуем
            // ВСЕГДА (пустые массивы = «прогресса не было»), независимо от успеха серверного репорта.
            int n = pending?.Count ?? 0;
            var types = new string[n];
            var amounts = new int[n];
            if (pending != null)
            {
                int i = 0;
                foreach (var kv in pending) { types[i] = kv.Key; amounts[i] = kv.Value; i++; }
            }
            GameEventBus.Publish(new MatchTasksFlushedUIEvent { Types = types, Amounts = amounts });

            if (pending == null) return;
            DailyService.ReportProgressBatch(pending);   // до входа — грейсфул no-op (гейтит FunctionService)
        }
    }
}
