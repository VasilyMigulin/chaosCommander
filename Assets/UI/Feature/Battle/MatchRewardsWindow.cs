using AwesomeUI.Core.Window;
using DG.Tweening;
using Game.Core.Backend;
using Game.Core.Events;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Feature.Battle
{
    /// <summary>
    /// Финальное окно послематчевого флоу (открывает MatchResultWindow кнопкой «Далее»):
    /// три секции, каждая скрывается, если пуста:
    ///   • золото за матч — из RatingUpdatedEvent (сервер выдаёт при расчёте Elo; может прийти и
    ///     ПОСЛЕ открытия окна — секция дорисуется обработчиком);
    ///   • карты PvE-энкаунтера (первое прохождение) — буфер RewardCardsGrantedUIEvent от BattleState;
    ///   • задачи за матч — дельты из MatchTasksFlushedUIEvent (флеш TaskTrackingService) + актуальные
    ///     состояния DailyService.GetState; переиспользуется слот журнала (DailyTaskSlot/TaskSlotView),
    ///     выполненные можно заклеймить прямо здесь (паттерн DailyPanel.OnClaimTask, без ревард-попапа —
    ///     кошелёк применяет сервис, слот перерисовывается).
    /// Кнопка «В меню» → ExitToMenuRequestedEvent (навигация на уровне States, как раньше).
    ///
    /// Persistent-канвас: подписки и сброс буферов в OnInject/Unject (см. MatchResultWindow).
    ///
    /// ПРЕФАБ (объект-сосед ResultWindow в BattlePanel.prefab): CanvasGroup + фон; _goldRoot
    /// (Image _goldIcon + TMP _goldText), _cardsRoot (layout-контейнер _cardsContainer + префаб
    /// MatchRewardCardItem), _tasksRoot (layout _tasksContainer + префаб TaskSlotView), _menuButton
    /// (LocalizedText ui.battle.result_menu). Все поля null-safe.
    /// </summary>
    public class MatchRewardsWindow : SourceWindow
    {
        [Header("UI refs")]
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private Button _menuButton;

        [Header("Золото за матч")]
        [SerializeField] private GameObject _goldRoot;
        [SerializeField] private Image _goldIcon;
        [SerializeField] private TextMeshProUGUI _goldText;

        [Header("Карты PvE (первое прохождение энкаунтера)")]
        [SerializeField] private GameObject _cardsRoot;
        [SerializeField] private Transform _cardsContainer;
        [SerializeField] private MatchRewardCardItem _cardItemPrefab;

        [Header("Задачи за матч (слот журнала)")]
        [SerializeField] private GameObject _tasksRoot;
        [SerializeField] private Transform _tasksContainer;
        [SerializeField] private DailyTaskSlot _taskSlotPrefab;
        [Tooltip("Опц.: текст «загрузка…», пока едет GetState.")]
        [SerializeField] private TextMeshProUGUI _tasksLoadingText;

        // ── Буферы текущего матча (копятся с MatchEndedEvent, показываются в ShowAfterMatch) ──
        readonly List<(Game.Core.Shared.CardVisualData visual, int count)> _cards = new();
        readonly Dictionary<string, int> _taskDeltas = new();
        bool _goldArrived;
        string _goldCode;
        int _goldAmount;

        readonly List<MatchRewardCardItem> _cardItems = new();
        readonly List<DailyTaskSlot> _taskSlots = new();
        bool _shown;
        bool _claimBusy;

        public override SourceWindow Init()
        {
            base.Init();
            if (_canvasGroup == null) _canvasGroup = GetComponent<CanvasGroup>();
            if (_menuButton != null) _menuButton.onClick.AddListener(OnMenu);
            gameObject.SetActive(false);
            return this;
        }

        public override void Dispose()
        {
            base.Dispose();
            if (_menuButton != null) _menuButton.onClick.RemoveListener(OnMenu);
        }

        public override void OnInject()
        {
            GameEventBus.Subscribe<RewardCardsGrantedUIEvent>(OnRewardCards);
            GameEventBus.Subscribe<MatchTasksFlushedUIEvent>(OnTasksFlushed);
            GameEventBus.Subscribe<RatingUpdatedEvent>(OnRatingUpdated);

            // Сброс ПРЕДЫДУЩЕГО матча (persistent-канвас).
            _cards.Clear();
            _taskDeltas.Clear();
            _goldArrived = false;
            _goldCode = null;
            _goldAmount = 0;
            _shown = false;
            _claimBusy = false;
            ClearSpawned();
            _canvasGroup?.DOKill();
            gameObject.SetActive(false);
        }

        public override void Unject()
        {
            GameEventBus.Unsubscribe<RewardCardsGrantedUIEvent>(OnRewardCards);
            GameEventBus.Unsubscribe<MatchTasksFlushedUIEvent>(OnTasksFlushed);
            GameEventBus.Unsubscribe<RatingUpdatedEvent>(OnRatingUpdated);
        }

        // ── Буферизация (события прилетают на MatchEndedEvent, окно откроется позже) ──────────

        void OnRewardCards(RewardCardsGrantedUIEvent e) => _cards.Add((e.Visual, Mathf.Max(1, e.Count)));

        void OnTasksFlushed(MatchTasksFlushedUIEvent e)
        {
            _taskDeltas.Clear();
            if (e.Types != null && e.Amounts != null)
                for (int i = 0; i < e.Types.Length && i < e.Amounts.Length; i++)
                    if (!string.IsNullOrEmpty(e.Types[i]) && e.Amounts[i] > 0)
                        _taskDeltas[e.Types[i]] = e.Amounts[i];
        }

        void OnRatingUpdated(RatingUpdatedEvent e)
        {
            _goldArrived = e.RewardAmount > 0 && !string.IsNullOrEmpty(e.RewardCode);
            _goldCode = e.RewardCode;
            _goldAmount = e.RewardAmount;
            if (_shown) RenderGold();   // расчёт мог доехать, когда окно уже открыто
        }

        // ── Открытие ─────────────────────────────────────────────────────────────

        /// <summary>Показать окно (зовёт MatchResultWindow на последнем «Далее»).</summary>
        public void ShowAfterMatch()
        {
            _shown = true;
            gameObject.SetActive(true);

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
                _canvasGroup.DOFade(1f, 0.3f);
            }

            RenderGold();
            RenderCards();
            LoadTasks();
        }

        void RenderGold()
        {
            if (_goldRoot == null) return;
            _goldRoot.SetActive(_goldArrived);
            if (!_goldArrived) return;

            if (_goldIcon != null)
            {
                var sprite = MetaIcon.Currency(_goldCode);
                _goldIcon.sprite = sprite;
                _goldIcon.enabled = sprite != null;
            }
            if (_goldText != null) _goldText.text = $"+{_goldAmount}";
        }

        void RenderCards()
        {
            if (_cardsRoot != null) _cardsRoot.SetActive(_cards.Count > 0);
            if (_cards.Count == 0 || _cardsContainer == null || _cardItemPrefab == null) return;

            foreach (var (visual, count) in _cards)
            {
                var item = Instantiate(_cardItemPrefab, _cardsContainer);
                item.gameObject.SetActive(true);
                item.Set(visual, count);
                _cardItems.Add(item);
            }
        }

        void LoadTasks()
        {
            bool any = _taskDeltas.Count > 0;
            if (_tasksRoot != null) _tasksRoot.SetActive(any);
            if (!any) return;

            if (_tasksLoadingText != null) _tasksLoadingText.gameObject.SetActive(true);

            DailyService.GetState(
                onSuccess: state =>
                {
                    if (!_shown) return;   // окно уже закрыто/переинжектнуто — не рисуем в пустоту
                    if (_tasksLoadingText != null) _tasksLoadingText.gameObject.SetActive(false);
                    SpawnTaskSlots(state);
                },
                onError: err =>
                {
                    Debug.LogWarning($"[MatchRewards] GetState failed: {err}");
                    if (_tasksLoadingText != null) _tasksLoadingText.gameObject.SetActive(false);
                    if (_tasksRoot != null) _tasksRoot.SetActive(false);   // без состояний бары не показать
                });
        }

        void SpawnTaskSlots(DailyService.DailyStateResponse state)
        {
            if (state == null || _tasksContainer == null || _taskSlotPrefab == null) return;

            ClearTaskSlots();
            SpawnBucket(state.Daily);
            SpawnBucket(state.Weekly);

            // Прогресс был, но все затронутые задачи уже забраны/не найдены — секцию прячем.
            if (_taskSlots.Count == 0 && _tasksRoot != null) _tasksRoot.SetActive(false);
        }

        void SpawnBucket(List<DailyService.TaskState> tasks)
        {
            if (tasks == null) return;
            foreach (var task in tasks)
            {
                if (task == null || !_taskDeltas.ContainsKey(task.Type)) continue;   // только затронутые матчем
                var slot = Instantiate(_taskSlotPrefab, _tasksContainer);
                slot.gameObject.SetActive(true);
                slot.Init();
                slot.SetData(task, OnClaimTask);
                _taskSlots.Add(slot);
            }
        }

        // Клейм прямо из окна (паттерн DailyPanel, без ревард-попапа): кошелёк применяет DailyService,
        // слот перерисовываем локально — GetState заново не дёргаем.
        void OnClaimTask(DailyService.TaskState task)
        {
            if (_claimBusy || task == null) return;
            _claimBusy = true;
            DailyService.ClaimTask(task.Id,
                onSuccess: resp =>
                {
                    _claimBusy = false;
                    if (resp != null && resp.Success)
                    {
                        task.Claimed = true;
                        task.Claimable = false;
                        foreach (var s in _taskSlots) if (s != null) s.UpdateView();
                        NotifyService.Info(UIStrings.Reward);
                    }
                    else NotifyService.Warning(resp != null ? resp.Reason : "claim failed");
                },
                onError: err => { _claimBusy = false; NotifyService.Warning(err); });
        }

        // ── Выход ────────────────────────────────────────────────────────────────

        void OnMenu()
        {
            _shown = false;
            // Навигация/teardown боя (дисконнект Photon + смена состояния) — на уровне States.
            GameEventBus.Publish(new ExitToMenuRequestedEvent());
        }

        // ── Уборка ───────────────────────────────────────────────────────────────

        void ClearSpawned()
        {
            foreach (var i in _cardItems) if (i != null) Destroy(i.gameObject);
            _cardItems.Clear();
            ClearTaskSlots();
        }

        void ClearTaskSlots()
        {
            foreach (var s in _taskSlots) { if (s == null) continue; s.Dispose(); Destroy(s.gameObject); }
            _taskSlots.Clear();
        }
    }
}
