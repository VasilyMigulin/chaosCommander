using System;
using System.Collections.Generic;
using AwesomeUI.Core.Panel;
using Game.Core.Backend;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// Ежедневные/еженедельные задачи + входные награды. Всё считает сервер по серверному времени
    /// (DailyService.GetState). Клейм наград — серверными функциями. Наследует ComingSoonPanel («Назад»).
    /// Открывается кнопкой «Журнал» в нижней навигации HUD (AwesomeButton с _targetPanelId=DailyPanel).
    ///
    /// Префаб:
    ///   Login:  _loginStripRoot (лента дней), _loginDaySlotPrefab (LoginRewardDaySlot),
    ///           _streakText ("Серия: 3"), _claimLoginBtn (забрать вход сегодня).
    ///   Tasks:  _dailyRoot, _weeklyRoot, _taskSlotPrefab (DailyTaskSlot).
    ///   Timers: _dailyTimerText, _weeklyTimerText (обратный отсчёт до сброса).
    ///   Shared: _rewardPopup (RewardPopupView), _loadingOverlay, _feedbackText.
    /// </summary>
    public class DailyPanel : ComingSoonPanel
    {
        [Header("Login rewards")]
        [SerializeField] private Transform _loginStripRoot;
        [SerializeField] private LoginRewardDaySlot _loginDaySlotPrefab;
        [SerializeField] private TextMeshProUGUI _streakText;
        [SerializeField] private Button _claimLoginBtn;

        [Header("Tasks")]
        [SerializeField] private Transform _dailyRoot;
        [SerializeField] private Transform _weeklyRoot;
        [SerializeField] private DailyTaskSlot _taskSlotPrefab;

        [Header("Timers")]
        [SerializeField] private TextMeshProUGUI _dailyTimerText;
        [SerializeField] private TextMeshProUGUI _weeklyTimerText;

        [Header("Shared")]
        [SerializeField] private RewardPopupView _rewardPopup;
        [SerializeField] private GameObject _loadingOverlay;
        [Tooltip("Общий контейнер всего контента журнала (вход/задачи/достижения). Прячется, пока грузим " +
                 "(крутится LoadingOverlay), показывается по ответу — чтобы не мелькали пустые секции.")]
        [SerializeField] private GameObject _contentRoot;
        [SerializeField] private TextMeshProUGUI _feedbackText;

        readonly List<LoginRewardDaySlot> _loginSlots = new();
        readonly List<DailyTaskSlot> _dailySlots = new();
        readonly List<DailyTaskSlot> _weeklySlots = new();

        bool _busy;
        int _dailyResetHourUtc;
        int _weeklyResetDay;
        int _weeklyResetHourUtc;
        bool _hasResets;

        public override void OnInject()
        {
            base.OnInject();
            if (_claimLoginBtn != null) _claimLoginBtn.onClick.AddListener(OnClaimLogin);
        }

        public override void OnOpen(params System.Action[] onComplete)
        {
            base.OnOpen(onComplete);
            Refresh();   // грузим при ОТКРЫТИИ (после логина), а не на init
        }

        /// <summary>Перечитать журнал с сервера. Публично — чтобы дев-меню обновляло открытую панель после репорта.</summary>
        public void Refresh()
        {
            ClearAll();
            SetLoading(true);
            DailyService.GetState(
                onSuccess: state =>
                {
                    SetLoading(false);
                    Populate(state);
                },
                onError: err => { SetLoading(false); ShowFeedback(err); });
        }

        void Populate(DailyService.DailyStateResponse state)
        {
            if (state == null) return;

            _dailyResetHourUtc = state.DailyResetHourUtc;
            _weeklyResetDay = state.WeeklyResetDay;
            _weeklyResetHourUtc = state.WeeklyResetHourUtc;
            _hasResets = true;

            // Login
            if (_streakText != null) _streakText.text = $"{UIStrings.Streak}: {state.Login.StreakDay}";
            if (_claimLoginBtn != null) _claimLoginBtn.interactable = state.Login.Available && !_busy;
            if (_loginStripRoot != null && _loginDaySlotPrefab != null && state.Login.Days != null)
            {
                foreach (var day in state.Login.Days)
                {
                    var slot = Instantiate(_loginDaySlotPrefab, _loginStripRoot);
                    slot.gameObject.SetActive(true);
                    slot.SetData(day);
                    _loginSlots.Add(slot);
                }
            }

            // Tasks
            PopulateTasks(state.Daily, _dailyRoot, _dailySlots);
            PopulateTasks(state.Weekly, _weeklyRoot, _weeklySlots);
        }

        void PopulateTasks(List<DailyService.TaskState> tasks, Transform root, List<DailyTaskSlot> bucket)
        {
            if (tasks == null || root == null || _taskSlotPrefab == null) return;
            foreach (var task in tasks)
            {
                var slot = Instantiate(_taskSlotPrefab, root);
                slot.gameObject.SetActive(true);
                slot.Init();
                slot.SetData(task, OnClaimTask);
                bucket.Add(slot);
            }
        }

        // ── Действия ─────────────────────────────────────────────────────────────

        void OnClaimLogin()
        {
            if (_busy) return;
            _busy = true;
            DailyService.ClaimLoginReward(
                onSuccess: resp =>
                {
                    _busy = false;
                    if (resp != null && resp.Success)
                    {
                        if (_rewardPopup != null) _rewardPopup.Show(resp.Reward, UIStrings.LoginReward);
                        Refresh();
                    }
                    else ShowFeedback(resp != null ? resp.Reason : "claim failed");
                },
                onError: err => { _busy = false; ShowFeedback(err); });
        }

        void OnClaimTask(DailyService.TaskState task)
        {
            if (_busy || task == null) return;
            _busy = true;
            DailyService.ClaimTask(task.Id,
                onSuccess: resp =>
                {
                    _busy = false;
                    if (resp != null && resp.Success)
                    {
                        if (_rewardPopup != null) _rewardPopup.Show(resp.Reward, UIStrings.Reward);
                        Refresh();
                    }
                    else ShowFeedback(resp != null ? resp.Reason : "claim failed");
                },
                onError: err => { _busy = false; ShowFeedback(err); });
        }

        void Update()
        {
            if (!_hasResets) return;
            // Весь текст в ОДИН TMP: «Сброс через 09:14:32» — префикс и отсчёт собирает код (без отдельной подписи).
            if (_dailyTimerText != null)
                _dailyTimerText.text = ResetLine(ServerClock.TimeUntil(ServerClock.NextDailyResetUtc(_dailyResetHourUtc)));
            if (_weeklyTimerText != null)
                _weeklyTimerText.text = ResetLine(ServerClock.TimeUntil(
                    ServerClock.NextWeeklyResetUtc((DayOfWeek)_weeklyResetDay, _weeklyResetHourUtc)));
        }

        static string ResetLine(TimeSpan t) => $"{UIStrings.JournalReset} {Format(t)}";

        // Дневной: «09:14:32» (секунды уместны — уже скоро). Недельный: «2д 14:22» (дни + Ч:М, без секунд —
        // тикающие секунды на многодневном отсчёте только дёргают глаз). Суффикс «д» локализуемый.
        static string Format(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            return t.Days > 0
                ? $"{t.Days}{UIStrings.DayShort} {t.Hours:00}:{t.Minutes:00}"
                : $"{t.Hours:00}:{t.Minutes:00}:{t.Seconds:00}";
        }

        // Грузим → крутим оверлей и ПРЯЧЕМ контент (не мелькают пустые секции); по ответу — наоборот.
        void SetLoading(bool on)
        {
            if (_loadingOverlay != null) _loadingOverlay.SetActive(on);
            if (_contentRoot != null) _contentRoot.SetActive(!on);
        }
        void ShowFeedback(string msg) { if (_feedbackText != null) _feedbackText.text = msg; }

        void ClearAll()
        {
            foreach (var s in _loginSlots) if (s != null) Destroy(s.gameObject);
            _loginSlots.Clear();
            ClearTasks(_dailySlots);
            ClearTasks(_weeklySlots);
        }

        void ClearTasks(List<DailyTaskSlot> bucket)
        {
            foreach (var s in bucket) { if (s == null) continue; s.Dispose(); Destroy(s.gameObject); }
            bucket.Clear();
        }

        public override void Unject()
        {
            base.Unject();
            if (_claimLoginBtn != null) _claimLoginBtn.onClick.RemoveListener(OnClaimLogin);
            ClearAll();
        }

        public override void OnDipose()
        {
            ClearAll();
            base.OnDipose();
        }
    }
}
