using AwesomeUI.Core.Attributes;
using AwesomeUI.Core.Window;
using DG.Tweening;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Shared;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Feature.Battle
{
    /// <summary>
    /// Окно мулигана.
    /// Показывает предложенные карты, даёт возможность выбрать карты для замены,
    /// затем подтвердить или пропустить мулиган.
    /// Открывается при MulliganStartedEvent, закрывается после подтверждения.
    /// </summary>
    public class MuliganWindow : SourceWindow
    {
        [UIInject] EcsWorld _world;
        [UIInject] IGameStateContext _state;

        [Header("Cards")]
        [SerializeField] private List<MuliganCardView> _cardViews;

        [Header("UI")]
        [SerializeField] private Button          _confirmButton;
        [SerializeField] private Button          _skipButton; 
        [SerializeField] private TextMeshProUGUI _replacementsLeftText;
        [SerializeField] private CanvasGroup     _canvasGroup;
        [SerializeField] private GameObject      _waitingPanel;

        private int   _playerEntity;
        private int   _maxReplacements;
        private int   _replacementsUsed;
        private readonly HashSet<int> _selectedEntities = new HashSet<int>();

        // Гейт открытия окна: MulliganStarted (в Init) готовит данные, но окно всплывает только
        // по MulliganPhaseBeginUIEvent (после VS-экрана) — чтобы VS был первым.
        private bool _readyToOpen;
        private bool _phaseBegun;
        private bool _opened;

        // ── Init / Lifecycle ──────────────────────────────────────────────────

        public override SourceWindow Init()
        {
            base.Init();
            _cardViews.ForEach(x => x.Init());
            if (_canvasGroup == null) _canvasGroup = GetComponent<CanvasGroup>();

            if (_confirmButton != null) _confirmButton.onClick.AddListener(OnConfirm);
            if (_skipButton != null) _skipButton.onClick.AddListener(OnSkip);

            return this;
        }

        public override void Dispose()
        {
            base.Dispose();

            if (_confirmButton != null) _confirmButton.onClick.RemoveListener(OnConfirm);
            if (_skipButton != null) _skipButton.onClick.RemoveListener(OnSkip);
        }

        // BattleCanvas — persistent (DontDestroyOnLoad): между матчами объект не пересоздаётся, Init()/Dispose()
        // второй раз не вызываются. А GameEventBus.Clear() (см. EcsRunHandler.Dispose при выходе из боя) обнуляет
        // ВСЮ шину — подписки, сделанные в Init(), пропадают навсегда после первого же матча (мулиган просто
        // переставал открываться на повторных боях). OnInject()/Unject() вызывает UIHandler НА КАЖДЫЙ матч
        // (см. BattleState.Start → UIModule.Inject) — здесь подписки переживают любое число матчей подряд.
        public override void OnInject()
        {
            GameEventBus.Subscribe<MulliganStartedEvent>(OnMulliganStarted);
            GameEventBus.Subscribe<MulliganCardReplacedEvent>(OnCardReplaced);
            GameEventBus.Subscribe<AllMulligansCompletedEvent>(OnAllMulligansCompleted);
            GameEventBus.Subscribe<PreStartPhaseBeginUIEvent>(OnPreStartPhaseBegin);
            // Открываемся не сразу по MulliganStarted, а по началу фазы мулигана (RPC_StartGame, после
            // VS-раскрытия) — чтобы VS-экран был ПЕРВЫМ, а мулиган после него.
            GameEventBus.Subscribe<MulliganPhaseBeginUIEvent>(OnMulliganPhaseBegin);

            // Чистый старт нового матча: прячем себя и сбрасываем гейты открытия сразу, не дожидаясь
            // PreStartPhaseBeginUIEvent — тот стреляет уже в конце матча.
            OnClose();
            _opened = false;
            _phaseBegun = false;
            _readyToOpen = false;

            // Восстановить UI, «сломанный» прошлым матчем (окно persistent, само не пересоздаётся):
            // OnConfirm() прячет обе кнопки (SetActive(false)), OnSkip()/AllMulligansCompleted оставляют
            // interactable=false и включённую плашку ожидания — без этого сброса на втором матче окно
            // мулигана открывалось без кнопок Confirm/Skip.
            if (_confirmButton != null) { _confirmButton.gameObject.SetActive(true); _confirmButton.interactable = false; }
            if (_skipButton != null)    { _skipButton.gameObject.SetActive(true);    _skipButton.interactable    = true; }
            if (_waitingPanel != null)  _waitingPanel.SetActive(false);
        }

        public override void Unject()
        {
            GameEventBus.Unsubscribe<MulliganStartedEvent>(OnMulliganStarted);
            GameEventBus.Unsubscribe<MulliganCardReplacedEvent>(OnCardReplaced);
            GameEventBus.Unsubscribe<AllMulligansCompletedEvent>(OnAllMulligansCompleted);
            GameEventBus.Unsubscribe<PreStartPhaseBeginUIEvent>(OnPreStartPhaseBegin);
            GameEventBus.Unsubscribe<MulliganPhaseBeginUIEvent>(OnMulliganPhaseBegin);
        }

        // ── Event handlers ────────────────────────────────────────────────────

        private void OnAllMulligansCompleted(AllMulligansCompletedEvent _)
        {
            // Блокируем UI мулигана и показываем плашку ожидания оппонента
            SetInteractable(false);
            if (_waitingPanel != null) _waitingPanel.SetActive(true);
        }

        private void OnPreStartPhaseBegin(PreStartPhaseBeginUIEvent _)
        {
            // Хост начал PreStart — закрываем мулиган
            OnClose();

            // BattleCanvas — persistent (DontDestroyOnLoad, см. UIModule/UIHandler.InvokeCanvas): окно
            // не пересоздаётся между матчами, Init() второй раз не вызывается. Без сброса этих трёх флагов
            // OpenNow() навсегда выходил бы по гварду _opened=true с ПЕРВОГО мулигана — окно мулигана
            // просто не открывалось бы ни на одном следующем матче (то же семейство бага, что и у
            // MatchResultWindow с застрявшим попапом результата).
            _opened = false;
            _phaseBegun = false;
            _readyToOpen = false;
        }

        private void OnMulliganStarted(MulliganStartedEvent evt)
        {
            _playerEntity     = evt.PlayerEntity;
            _maxReplacements  = evt.MaxReplacements;
            _replacementsUsed = 0;
            _selectedEntities.Clear();
            _confirmButton.interactable = false;

            for (int i = 0; i < _cardViews.Count; i++)
            {
                if (i < evt.OfferedCardEntities.Length)
                {
                    CardVisualData? visual = evt.OfferedCardVisuals != null && i < evt.OfferedCardVisuals.Length
                        ? evt.OfferedCardVisuals[i]
                        : (CardVisualData?)null;
                    _cardViews[i].Setup(evt.OfferedCardEntities[i], visual, $"Card {i + 1}", OnCardToggled);
                }
                else
                    _cardViews[i].Clear();
            }

            UpdateReplacementsLabel();

            // Данные готовы, но окно открываем только когда началась фаза мулигана (после VS-экрана).
            _readyToOpen = true;
            UnityEngine.Debug.Log($"[MuliganWindow] OnMulliganStarted: offered={evt.OfferedCardEntities?.Length ?? 0} _phaseBegun={_phaseBegun} → readyToOpen=true");
            if (_phaseBegun) OpenNow();
        }

        // Начало фазы мулигана (RPC_StartGame, после показа VS): теперь можно показать окно.
        private void OnMulliganPhaseBegin(MulliganPhaseBeginUIEvent _)
        {
            _phaseBegun = true;
            UnityEngine.Debug.Log($"[MuliganWindow] OnMulliganPhaseBegin: _readyToOpen={_readyToOpen} _opened={_opened}");
            if (_readyToOpen) OpenNow();
        }

        private void OpenNow()
        {
            UnityEngine.Debug.Log($"[MuliganWindow] OpenNow: opened_before={_opened}");
            if (_opened) return;
            _opened = true;
            OnOpen();
        }

        private void OnCardReplaced(MulliganCardReplacedEvent evt)
        {
            for (int i = 0; i < _cardViews.Count; i++)
            {
                var view = _cardViews[i];

                if (!view.gameObject.activeSelf) continue;

                for (int j = 0; j < evt.OldCardEntity.Length; j++)
                { 
                    int oldCardEntity = evt.OldCardEntity[j];
                    int newCardEntity = evt.NewCardEntity[j];
                    CardVisualData cardVisualData = evt.NewCardVisual[j];

                    if (view.CardEntity != oldCardEntity) continue;

                    _selectedEntities.Remove(oldCardEntity);

                    view.Setup(newCardEntity, cardVisualData, "New Card", OnCardToggled);
                    view.Lock();
                    break;
                } 
            } 
        }

        // ── Card selection ────────────────────────────────────────────────────

        private void OnCardToggled(MuliganCardView view)
        {
            if (view.IsSelected)
            {
                _selectedEntities.Add(view.CardEntity);
                _replacementsUsed++;
            }
            else
            {
                _selectedEntities.Remove(view.CardEntity);
                _replacementsUsed--;
                view.ResetHighlight();
            }

            if (_replacementsUsed == _maxReplacements)
            {
                foreach (var cardView in _cardViews)
                {
                    if (cardView == view) continue;
                    if (cardView.IsSelected) continue;
                    cardView.Lock();
                }
            }
            else
            {
                foreach (var cardView in _cardViews)
                {
                    if (cardView == view) continue;
                    if (cardView.IsSelected) continue;
                    cardView.Unlock();
                }
            }

            if (_confirmButton != null)
            {
                _confirmButton.interactable = _replacementsUsed > 0;
            }

            UpdateReplacementsLabel();
        }

        // ── Buttons ─────────────────────────────────────────────────────────── 
        private void OnConfirm()
        { 
            _world.GetPool<MulliganReplaceRequest>().Add(_playerEntity).CardEntities = _selectedEntities.ToArray();

            _skipButton.gameObject.SetActive(false);
            _confirmButton.gameObject.SetActive(false);
        }

        private void OnSkip()
        {
            SetInteractable(false);
            PublishCompleted();
        }

        private void PublishCompleted()
        {
            _world.GetPool<MulliganCompleteEvent>().Add(_playerEntity);
        }

        // ── Show / Hide ───────────────────────────────────────────────────────

        public override void OnOpen()
        {
            gameObject.SetActive(true);

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
                _canvasGroup.DOFade(1f, 0.3f);
            }
        }

        public override void OnClose()
        {
            if (_canvasGroup != null)
            {
                _canvasGroup.DOFade(0f, 0.25f)
                    .OnComplete(() => gameObject.SetActive(false));
            }
            else
            {
                gameObject.SetActive(false);
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void SetInteractable(bool value)
        {
            if (_confirmButton != null) _confirmButton.interactable = value;
            if (_skipButton    != null) _skipButton.interactable    = value;
            foreach (var cardView in _cardViews)
            {
                if (value) cardView.Unlock();
                else       cardView.Lock();
            }
        }

        private void UpdateReplacementsLabel()
        {
            if (_replacementsLeftText != null)
                _replacementsLeftText.text = $"{_replacementsUsed}/{_maxReplacements}";
        }
    }
}
