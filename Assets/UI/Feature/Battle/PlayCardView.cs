using AwesomeUI.Core.Card;
using AwesomeUI.Core.Slot;
using DG.Tweening;
using Game.Core.Events;
using Game.Core.Shared;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AwesomeUI.Feature.Battle
{
    /// <summary>
    /// Базовый View карты в руке игрока.
    /// Хранит данные карты, отображает полную визуализацию через CardBaseView.
    /// Производные классы — CommanderCardView и SimpleCardView.
    ///
    /// Подсветка через CardHighlightEffect (один шейдерный компонент):
    ///   Selection    — карта удерживается игроком     (синий)
    ///   Affordable   — на карту хватает ресурсов      (зелёный)
    ///   AbilityReady — у карты активировался эффект   (оранжевый)
    ///
    /// Розыгрыш через drag-and-drop:
    ///   PointerDown  — начинаем drag (только если карта доступна)
    ///   Drag         — перемещаем карту за курсором
    ///   PointerUp    — если карта за пределами layout → розыгрыш,
    ///                  иначе → возврат на исходную позицию с анимацией
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public abstract class PlayCardView : CardBaseView,
        IPointerDownHandler, IDragHandler, IEndDragHandler
    {
        [Header("Drag Settings")]
        [SerializeField] private float _returnDuration = 0.25f;

        public int  CardEntity { get; private set; }
        public bool IsOccupied { get; private set; }
        public bool IsSelected { get; private set; }

        protected PlayCardData _data;
        protected CanvasGroup  _canvasGroup;

        private bool _isAffordable;
        private bool _isAbilityReady;

        private bool          _isDragging;
        private bool          _pendingPlay;   // карта скрыта, ждёт завершения каста
        private Vector3       _dragStartLocalPos;
        private RectTransform _rectTransform;
        private Canvas        _canvas;
        private RectTransform _layoutRect;  // RectTransform родительского CardLayout

        // ── Init / Dispose ───────────────────────────────────────────────────

        public override SourceSlot Init()
        {
            base.Init();
            _canvasGroup  = GetComponent<CanvasGroup>();
            _rectTransform = GetComponent<RectTransform>();
            _canvas        = GetComponentInParent<Canvas>();
            _layoutRect    = transform.parent?.GetComponent<RectTransform>();
            ResetHighlight();
            gameObject.SetActive(false);
            return this;
        }

        public override void OnInject()
        {
            GameEventBus.Subscribe<CardAffordableChangedEvent>(OnAffordableChanged);
            GameEventBus.Subscribe<CardAbilityReadyChangedEvent>(OnAbilityReadyChanged);
            GameEventBus.Subscribe<TargetSelectionCancelledEvent>(OnTargetSelectionCancelled);
        }

        public override void Unject()
        {
            GameEventBus.Unsubscribe<CardAffordableChangedEvent>(OnAffordableChanged);
            GameEventBus.Unsubscribe<CardAbilityReadyChangedEvent>(OnAbilityReadyChanged);
            GameEventBus.Unsubscribe<TargetSelectionCancelledEvent>(OnTargetSelectionCancelled);
        }

        public override void Dispose()
        {
            Unject();
            base.Dispose();
            ClearCard();
        }

        // ── Card Data ────────────────────────────────────────────────────────

        /// <summary>Заполнить View данными карты и активировать.</summary>
        public virtual void SetCard(PlayCardData data)
        {
            _data      = data;
            CardEntity = data.CardEntity;
            IsOccupied = true;
            IsSelected = false;
            _isAffordable   = false;
            _isAbilityReady = false;
            _isDragging     = false;

            _layoutRect = transform.parent?.GetComponent<RectTransform>();

            if (_icon != null && data.Icon != null)
                _icon.sprite = data.Icon;

            gameObject.SetActive(true);
            UpdateView();
            GameEventBus.Publish(new CardPlacedInHandViewEvent { CardEntity = CardEntity });
        }

        /// <summary>Очистить View и деактивировать для повторного использования.</summary>
        public virtual void ClearCard()
        {
            _isDragging  = false;
            _pendingPlay = false;
            if (_canvasGroup != null) _canvasGroup.blocksRaycasts = true;
            gameObject?.SetActive(false);
            CardEntity      = -1;
            IsOccupied      = false;
            IsSelected      = false;
            _isAffordable   = false;
            _isAbilityReady = false;
            ResetHighlight();
        }

        // ── Drag & Drop ──────────────────────────────────────────────────────

        public void OnPointerDown(PointerEventData eventData)
        {
            Debug.Log($"[PlayCardView] OnPointerDown card={CardEntity} occupied={IsOccupied} affordable={_isAffordable}");
            if (!IsOccupied || !_isAffordable) return;

            _isDragging        = true;
            _dragStartLocalPos = _rectTransform.localPosition;

            _rectTransform.DOKill();

            IsSelected = true;
            SetHighlight(CardHighlightEffect.HighlightType.Selection, true);

            // Карта поверх остальных во время перетаскивания
            _rectTransform.SetAsLastSibling();

            // Не блокируем рейкасты во время перетаскивания
            _canvasGroup.blocksRaycasts = false;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_isDragging) return;

            Vector2 delta = eventData.delta;
            if (_canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                delta /= _canvas.scaleFactor;

            _rectTransform.localPosition += new Vector3(delta.x, delta.y, 0f);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            Debug.Log($"[PlayCardView] OnEndDrag card={CardEntity} isDragging={_isDragging}");
            if (!_isDragging) return;
            _isDragging = false;
            _canvasGroup.blocksRaycasts = true;

            bool outside = IsOutsideLayout();
            Debug.Log($"[PlayCardView] IsOutsideLayout={outside} layoutRect={_layoutRect}");
            if (outside)
            {
                // Скрываем карту немедленно, розыгрыш или отмена завершат остальное
                IsSelected    = false;
                _pendingPlay  = true;
                gameObject.SetActive(false);
                OnActive();
            }
            else
            {
                // Возврат на место
                IsSelected = false;
                SetHighlight(CardHighlightEffect.HighlightType.Selection, false);
                _rectTransform.DOLocalMove(_dragStartLocalPos, _returnDuration)
                    .SetEase(Ease.OutCubic);
            }
        }

        private bool IsOutsideLayout()
        {
            if (_layoutRect == null) return false;
            Camera cam = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _canvas.worldCamera : null;
            Vector3 worldPos = _rectTransform.position;
            return !RectTransformUtility.RectangleContainsScreenPoint(
                _layoutRect,
                RectTransformUtility.WorldToScreenPoint(cam, worldPos),
                cam);
        }

        // ── Highlights ───────────────────────────────────────────────────────

        public override void OnActive()
        {
            Debug.Log($"[PlayCardView] OnActive publishing CardPlayRequestedEvent card={CardEntity}");
            GameEventBus.Publish(new CardPlayRequestedEvent { CardEntity = CardEntity });
        }

        public void Deselect()
        {
            IsSelected = false;
            SetHighlight(CardHighlightEffect.HighlightType.Selection, false);
        }

        private void OnTargetSelectionCancelled(TargetSelectionCancelledEvent evt)
        {
            if (!_pendingPlay || evt.CardEntity != CardEntity) return;
            // Выбор цели отменён — возвращаем карту в руку
            _pendingPlay = false;
            _rectTransform.localPosition = _dragStartLocalPos;
            gameObject.SetActive(true);
            UpdateView();
        }

        private void OnAffordableChanged(CardAffordableChangedEvent evt)
        {
            if (evt.CardEntity != CardEntity) return;
            Debug.Log($"[PlayCardView] OnAffordableChanged card={CardEntity} affordable={evt.IsAffordable}");
            _isAffordable = evt.IsAffordable;
            SetHighlight(CardHighlightEffect.HighlightType.Affordable, _isAffordable);
        }

        private void OnAbilityReadyChanged(CardAbilityReadyChangedEvent evt)
        {
            if (evt.CardEntity != CardEntity) return;
            _isAbilityReady = evt.IsReady;
            SetHighlight(CardHighlightEffect.HighlightType.AbilityReady, _isAbilityReady);
        }

        // ── UpdateView ───────────────────────────────────────────────────────

        public override void UpdateView()
        {
            if (_icon != null)
            {
                _icon.sprite  = _data.Icon;
                _icon.enabled = _data.Icon != null;
            }

            ApplyVisualData(_data.Visual);

            SetHighlight(CardHighlightEffect.HighlightType.Affordable,   _isAffordable);
            SetHighlight(CardHighlightEffect.HighlightType.AbilityReady, _isAbilityReady);
            SetHighlight(CardHighlightEffect.HighlightType.Selection,    IsSelected);
        }
    }
}
