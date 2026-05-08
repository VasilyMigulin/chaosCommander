using AwesomeUI.Core.Window;
using DG.Tweening;
using Game.Core.Events;
using System.Collections.Generic;
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
        [Header("Cards")]
        [SerializeField] private List<MuliganCardView> _cardViews;

        [Header("UI")]
        [SerializeField] private Button          _confirmButton;
        [SerializeField] private Button          _skipButton;
        [SerializeField] private TextMeshProUGUI _titleText;
        [SerializeField] private TextMeshProUGUI _replacementsLeftText;
        [SerializeField] private CanvasGroup     _canvasGroup;

        private int   _playerEntity;
        private int   _maxReplacements;
        private int   _replacementsUsed;
        private readonly HashSet<int> _selectedEntities = new HashSet<int>();

        // ── Init / Lifecycle ──────────────────────────────────────────────────

        public override SourceWindow Init()
        {
            base.Init();
            if (_canvasGroup == null) _canvasGroup = GetComponent<CanvasGroup>();
            return this;
        }

        public override void OnInject()
        {
            GameEventBus.Subscribe<MulliganStartedEvent>(OnMulliganStarted);
            GameEventBus.Subscribe<MulliganCardReplacedEvent>(OnCardReplaced);

            if (_confirmButton != null) _confirmButton.onClick.AddListener(OnConfirm);
            if (_skipButton    != null) _skipButton.onClick.AddListener(OnSkip);
        }

        public override void Unject()
        {
            GameEventBus.Unsubscribe<MulliganStartedEvent>(OnMulliganStarted);
            GameEventBus.Unsubscribe<MulliganCardReplacedEvent>(OnCardReplaced);

            if (_confirmButton != null) _confirmButton.onClick.RemoveListener(OnConfirm);
            if (_skipButton    != null) _skipButton.onClick.RemoveListener(OnSkip);
        }

        // ── Event handlers ────────────────────────────────────────────────────

        private void OnMulliganStarted(MulliganStartedEvent evt)
        {
            _playerEntity     = evt.PlayerEntity;
            _maxReplacements  = evt.MaxReplacements;
            _replacementsUsed = 0;
            _selectedEntities.Clear();

            for (int i = 0; i < _cardViews.Count; i++)
            {
                if (i < evt.OfferedCardEntities.Length)
                    _cardViews[i].Setup(evt.OfferedCardEntities[i], null, $"Card {i + 1}", OnCardToggled);
                else
                    _cardViews[i].Clear();
            }

            UpdateReplacementsLabel();
            OnOpen();
        }

        private void OnCardReplaced(MulliganCardReplacedEvent evt)
        {
            if (evt.PlayerEntity != _playerEntity) return;

            for (int i = 0; i < _cardViews.Count; i++)
            {
                var view = _cardViews[i];
                if (!view.gameObject.activeSelf) continue;
                if (view.CardEntity != evt.OldCardEntity) continue;

                _selectedEntities.Remove(evt.OldCardEntity);
                view.Setup(evt.NewCardEntity, null, "New Card", OnCardToggled);
                break;
            }

            _replacementsUsed++;
            UpdateReplacementsLabel();
        }

        // ── Card selection ────────────────────────────────────────────────────

        private void OnCardToggled(MuliganCardView view)
        {
            if (view.IsSelected)
                _selectedEntities.Add(view.CardEntity);
            else
                _selectedEntities.Remove(view.CardEntity);

            if (_confirmButton != null)
                _confirmButton.interactable = _replacementsUsed < _maxReplacements || _selectedEntities.Count == 0;
        }

        // ── Buttons ───────────────────────────────────────────────────────────

        private void OnConfirm()
        {
            foreach (var entity in _selectedEntities)
            {
                GameEventBus.Publish(new MulliganReplaceRequestedUIEvent
                {
                    PlayerEntity = _playerEntity,
                    CardEntity   = entity
                });
            }

            PublishCompleted();
        }

        private void OnSkip()
        {
            PublishCompleted();
        }

        private void PublishCompleted()
        {
            GameEventBus.Publish(new MulliganCompletedEvent { PlayerEntity = _playerEntity });
            OnClose();
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

        private void UpdateReplacementsLabel()
        {
            if (_replacementsLeftText != null)
                _replacementsLeftText.text = $"{_maxReplacements - _replacementsUsed}/{_maxReplacements}";
        }
    }
}
