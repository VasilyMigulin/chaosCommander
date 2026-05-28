using AwesomeUI.Core.Window;
using DG.Tweening;
using Game.Core.Events;
using Game.Core.Shared;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Feature.Battle
{
    /// <summary>
    /// Окно раскопки (discover / excavate).
    /// Открывается по CardPickOfferedEvent, показывает предложенные карты.
    /// Клик по карте → CardPickChosenEvent (выбор), кнопка отмены → CardPickCancelledEvent.
    /// Логику разрешения выбора (в т.ч. сетевую репликацию) ведёт CardPickSelectionSystem.
    ///
    /// Окно — дочернее у BattlePanel; SourcePanel.Init() авто-вызывает Init()/Dispose(),
    /// поэтому подписки живут здесь и отдельной регистрации в BattlePanel не требуется.
    /// </summary>
    public class PickupWindow : SourceWindow
    {
        [Header("Cards")]
        [SerializeField] private List<CardPickupView> _cardViews = new List<CardPickupView>();

        [Header("UI")]
        [SerializeField] private Button      _cancelButton;
        [SerializeField] private CanvasGroup _canvasGroup;

        private int _castingCardEntity = -1;

        public override SourceWindow Init()
        {
            base.Init();
            _cardViews.ForEach(v => v.Init());
            if (_canvasGroup == null) _canvasGroup = GetComponent<CanvasGroup>();

            GameEventBus.Subscribe<CardPickOfferedEvent>(OnOffered);

            if (_cancelButton != null) _cancelButton.onClick.AddListener(OnCancel);

            gameObject.SetActive(false);
            return this;
        }

        public override void Dispose()
        {
            base.Dispose();
            GameEventBus.Unsubscribe<CardPickOfferedEvent>(OnOffered);
            if (_cancelButton != null) _cancelButton.onClick.RemoveListener(OnCancel);
        }

        public override void Unject() { }

        // ── Events ───────────────────────────────────────────────────────────────

        private void OnOffered(CardPickOfferedEvent evt)
        {
            _castingCardEntity = evt.CastingCardEntity;

            for (int i = 0; i < _cardViews.Count; i++)
            {
                bool hasCard = evt.OfferedCardEntities != null
                    && i < evt.OfferedCount
                    && i < evt.OfferedCardEntities.Length;

                if (hasCard)
                {
                    CardVisualData? visual = evt.OfferedCardVisuals != null && i < evt.OfferedCardVisuals.Length
                        ? evt.OfferedCardVisuals[i]
                        : (CardVisualData?)null;

                    _cardViews[i].Setup(evt.OfferedCardEntities[i], visual, OnCardPicked);
                }
                else
                {
                    _cardViews[i].Clear();
                }
            }

            OnOpen();
        }

        private void OnCardPicked(CardPickupView view)
        {
            GameEventBus.Publish(new CardPickChosenEvent
            {
                CastingCardEntity = _castingCardEntity,
                ChosenCardEntity  = view.CardEntity,
            });

            OnClose();
        }

        private void OnCancel()
        {
            GameEventBus.Publish(new CardPickCancelledEvent { CastingCardEntity = _castingCardEntity });
            OnClose();
        }

        // ── Show / Hide ───────────────────────────────────────────────────────────

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
                _canvasGroup.DOFade(0f, 0.2f)
                    .OnComplete(() => gameObject.SetActive(false));
            }
            else
            {
                gameObject.SetActive(false);
            }
        }
    }
}
