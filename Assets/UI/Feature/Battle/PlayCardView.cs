using AwesomeUI.Core.Card;
using AwesomeUI.Core.Slot;
using DG.Tweening;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Shared;
using Game.Core.Shared.Interface;
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
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [Core.Attributes.UIInject] protected IGameStateContext _state;

        [Header("Drag Settings")]
        [SerializeField] private float _returnDuration = 0.25f;

        [Header("Hold Zoom (предпросмотр карты в руке удержанием)")]
        [Tooltip("Во сколько раз увеличить карту при удержании (чтобы прочитать текст).")]
        [SerializeField] private float _zoomScale = 1.6f;
        [Tooltip("Подъём карты вверх при зуме, px — чтобы палец не закрывал текст.")]
        [SerializeField] private float _zoomLift = 90f;
        [SerializeField] private float _zoomDuration = 0.15f;

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
        private int           _dragStartSiblingIndex; // позиция в иерархии до перетаскивания
        private RectTransform _rectTransform;
        private Canvas        _canvas;
        private RectTransform _layoutRect;  // RectTransform родительского CardLayout

        // ── Драг-розыгрыш существа «под пальцем» (мост через bus, UI Mono-кода не знает) ──
        // UI шлёт CreatureDragMoved/Released; CreatureDragPreviewSystem ведёт превью-модель и отвечает
        // CreatureDragOverFieldChangedEvent (карта растворяется/проявляется). Коммит/отмена — на системе.
        private bool _creatureDrag;      // система сказала «карта над полем, превью активно»
        private bool _dragToPlace;       // система сказала «это существо: размещение ТОЛЬКО дропом на поле»

        // ── Zoom-предпросмотр (hold-механика CardBaseView: OnHoldTriggered/OnHoldReleased) ──
        // Работает для ЛЮБОЙ карты руки — доступной и недоступной к розыгрышу (это чтение, не действие).
        private bool    _zoomActive;
        private int     _zoomSiblingIndex;
        private Vector3 _zoomStartLocalPos;

        /// <summary>Удержание ≥ порога: карта увеличивается, приподнимается и выходит на первый план —
        /// прочитать текст. Розыгрыш при этом не стартует (драг начнётся только с движения пальца).</summary>
        protected override void OnHoldTriggered()
        {
            if (!IsOccupied || _isDragging || _pendingPlay || _zoomActive) return;

            _zoomActive        = true;
            _zoomSiblingIndex  = _rectTransform.GetSiblingIndex();
            _zoomStartLocalPos = _rectTransform.localPosition;

            _rectTransform.SetAsLastSibling();   // поверх соседних карт
            _rectTransform.DOKill();
            _rectTransform.DOScale(_zoomScale, _zoomDuration).SetEase(Ease.OutCubic);
            _rectTransform.DOLocalMoveY(_zoomStartLocalPos.y + _zoomLift, _zoomDuration).SetEase(Ease.OutCubic);
        }

        /// <summary>Палец отпущен/увели после сработавшего удержания — плавно вернуть карту на место.</summary>
        protected override void OnHoldReleased() => CancelZoom(instant: false);

        // Схлопнуть зум и ГАРАНТИРОВАННО вернуть масштаб/позицию/порядок. instant — без твинов
        // (перед стартом драга и на деактивации: DOTween на неактивном объекте не играет).
        // Позиция восстанавливается в запомненную на момент зума; если рука успела перестроиться
        // (добор во время удержания) — ближайший релэйаут CardLayout всё равно расставит веер заново.
        private void CancelZoom(bool instant)
        {
            if (!_zoomActive) return;
            _zoomActive = false;

            _rectTransform.DOKill();
            _rectTransform.SetSiblingIndex(_zoomSiblingIndex);

            if (instant || !gameObject.activeInHierarchy)
            {
                _rectTransform.localScale    = Vector3.one;
                _rectTransform.localPosition = _zoomStartLocalPos;
                return;
            }

            _rectTransform.DOScale(1f, _zoomDuration).SetEase(Ease.OutCubic);
            _rectTransform.DOLocalMove(_zoomStartLocalPos, _zoomDuration).SetEase(Ease.OutCubic);
        }

        // Страховка от «застрявшего» зума: объект выключили любым путём (розыгрыш, дискард эффектом
        // оппонента, конец матча) — мгновенно вернуть трансформ к базе.
        private void OnDisable() => CancelZoom(instant: true);

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
            GameEventBus.Subscribe<CardCostChangedEvent>(OnCostChanged);
            GameEventBus.Subscribe<HandCardStatsChangedUIEvent>(OnHandStatsChanged);
            GameEventBus.Subscribe<TargetSelectionCancelledEvent>(OnTargetSelectionCancelled);
            GameEventBus.Subscribe<CreatureDragOverFieldChangedEvent>(OnDragOverFieldChanged);
            GameEventBus.Subscribe<CreatureDragStartedEvent>(OnCreatureDragStarted);
        }

        public override void Unject()
        {
            GameEventBus.Unsubscribe<CardAffordableChangedEvent>(OnAffordableChanged);
            GameEventBus.Unsubscribe<CardAbilityReadyChangedEvent>(OnAbilityReadyChanged);
            GameEventBus.Unsubscribe<CardCostChangedEvent>(OnCostChanged);
            GameEventBus.Unsubscribe<HandCardStatsChangedUIEvent>(OnHandStatsChanged);
            GameEventBus.Unsubscribe<TargetSelectionCancelledEvent>(OnTargetSelectionCancelled);
            GameEventBus.Unsubscribe<CreatureDragOverFieldChangedEvent>(OnDragOverFieldChanged);
            GameEventBus.Unsubscribe<CreatureDragStartedEvent>(OnCreatureDragStarted);
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
            _creatureDrag   = false;
            _dragToPlace    = false;

            // Сброс растворения: слот КОМАНДИРА не проходит через ClearCard (переиспользуется SetCard'ом
            // напрямую при возврате в руку после смерти), и после драг-розыгрыша (карта растворилась над
            // полем: альфа 0 / dissolve 1) командир возвращался НЕВИДИМЫМ.
            if (_canvasGroup != null) { _canvasGroup.DOKill(); _canvasGroup.alpha = 1f; _canvasGroup.blocksRaycasts = true; }
            GetComponent<CardDissolveDriver>()?.ResetInstant();

            // Новая карта в слоте — никакого унаследованного зума: флаг долой, масштаб к базе.
            _zoomActive = false;
            if (_rectTransform != null) { _rectTransform.DOKill(); _rectTransform.localScale = Vector3.one; }

            _layoutRect = transform.parent?.GetComponent<RectTransform>();

            if (_icon != null && data.Icon != null)
                _icon.sprite = data.Icon;

            gameObject.SetActive(true);
            UpdateView();
            GameEventBus.Publish(new CardPlacedInHandViewEvent { CardEntity = CardEntity });
        }

        /// <summary>
        /// Анимация дискарда: уменьшение + поворот + затухание + сдвиг вниз.
        /// По завершении вызывает onComplete (CardLayout очищает слот и пересчитывает веер).
        /// </summary>
        public void PlayDiscardAnimation(System.Action onComplete, float duration = 0.45f)
        {
            _zoomActive = false;   // дискард (в т.ч. эффектом оппонента ПОКА карту держат) главнее зума
            _rectTransform.DOKill();
            if (_canvasGroup != null) _canvasGroup.blocksRaycasts = false;

            var seq = DOTween.Sequence();
            seq.Append(_rectTransform.DOScale(0.5f, duration).SetEase(Ease.InCubic));
            seq.Join(_rectTransform.DOLocalRotate(new Vector3(0f, 0f, 35f), duration).SetEase(Ease.OutCubic));
            seq.Join(_rectTransform.DOLocalMoveY(_rectTransform.localPosition.y - 180f, duration).SetEase(Ease.InCubic));
            if (_canvasGroup != null) seq.Join(_canvasGroup.DOFade(0f, duration));
            seq.OnComplete(() =>
            {
                if (_canvasGroup != null) _canvasGroup.alpha = 1f;
                _rectTransform.localScale = Vector3.one;
                _rectTransform.localRotation = Quaternion.identity;
                onComplete?.Invoke();
            });
        }

        /// <summary>Очистить View и деактивировать для повторного использования.</summary>
        public virtual void ClearCard()
        {
            // Компонент уже уничтожен (teardown сцены/выход из приложения: SourceLayout.Dispose приходит
            // после разрушения объектов) — обращение к .gameObject кинуло бы NullReferenceException прямо
            // в UnityEngine.Component.get_gameObject. `gameObject?.` тут НЕ спасает: бросает сам геттер,
            // а не разыменование. Проверка через перегруженный Unity-оператор == ловит и fake-null.
            if (this == null) return;

            _isDragging  = false;
            _pendingPlay = false;
            _creatureDrag = false;
            _dragToPlace  = false;
            if (_canvasGroup != null) { _canvasGroup.DOKill(); _canvasGroup.alpha = 1f; _canvasGroup.blocksRaycasts = true; }
            gameObject.SetActive(false);
            CardEntity      = -1;
            IsOccupied      = false;
            IsSelected      = false;
            _isAffordable   = false;
            _isAbilityReady = false;
            ResetHighlight();
        }

        // ── Drag & Drop ──────────────────────────────────────────────────────

        // Старт ТОЛЬКО при реальном перетаскивании. Чистый клик (без движения) сюда
        // не попадает — поэтому карта не залипает в «выбранном» состоянии и не теряет рейкасты.
        public void OnBeginDrag(PointerEventData eventData)
        {
            Debug.Log($"[PlayCardView] OnBeginDrag card={CardEntity} occupied={IsOccupied} affordable={_isAffordable}");

            // Палец двинулся достаточно для драга — зум-предпросмотр схлопывается МГНОВЕННО и ДО записи
            // стартовой позиции/индекса ниже (иначе драг запомнил бы зумнутый масштаб/поднятую позицию/
            // «поверх всех» и они «сохранились» бы после возврата карты в руку). Для недоступной карты
            // ранний return ниже просто оставит карту на месте — зум уже снят, розыгрыш не начнётся.
            CancelZoom(instant: true);

            if (!IsOccupied || !_isAffordable) return;

            _isDragging            = true;
            _creatureDrag          = false;
            _dragToPlace           = false;   // система скажет заново для ЭТОГО драга (CreatureDragStartedEvent)
            _dragStartLocalPos     = _rectTransform.localPosition;
            _dragStartSiblingIndex = _rectTransform.GetSiblingIndex();

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

            // Драг-превью существа: сырой ввод → системе (для не-существ она игнорит и не отвечает).
            GameEventBus.Publish(new CreatureDragMovedEvent
            {
                CardEntity     = CardEntity,
                ScreenPosition = eventData.position,
            });
        }

        // Система → UI (раз на драг): тащим СУЩЕСТВО — размещение только дропом на поле; отпускание вне
        // поля вернёт карту в руку (старый клик-путь для существ больше не включается).
        private void OnCreatureDragStarted(CreatureDragStartedEvent evt)
        {
            if (evt.CardEntity != CardEntity || !_isDragging) return;
            _dragToPlace = true;
        }

        // Система → UI: карта над полем → растворяемся (превью существа под пальцем); ушла → проявляемся.
        // OverField=false принимаем и БЕЗ активного драга: если драг оборвался (релэйаут руки/блок ввода),
        // stale-уборка системы шлёт false — карта не должна остаться растворённой.
        private void OnDragOverFieldChanged(CreatureDragOverFieldChangedEvent evt)
        {
            if (evt.CardEntity != CardEntity) return;
            if (evt.OverField && !_isDragging) return;   // растворяться можно только в живом драге
            if (evt.OverField == _creatureDrag) return;
            _creatureDrag = evt.OverField;
            DissolveCard(evt.OverField);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            Debug.Log($"[PlayCardView] OnEndDrag card={CardEntity} isDragging={_isDragging}");
            if (!_isDragging) return;
            _isDragging = false;
            _canvasGroup.blocksRaycasts = true;

            // Возвращаем исходную позицию в иерархии (после SetAsLastSibling при старте),
            // иначе карта остаётся поверх соседей при возврате в руку.
            _rectTransform.SetSiblingIndex(_dragStartSiblingIndex);

            // ВСЕГДА сообщаем системе «палец отпущен» — иначе её пер-драг стейт (_card) утекал, когда драг
            // кончался НЕ над полем (возврат в руку / OnUse), и превью переставало работать для ДРУГИХ карт
            // до конца матча. Не над полем система ресетится молча (без Cancel) — безопасно для всех веток.
            GameEventBus.Publish(new CreatureDragReleasedEvent
            {
                CardEntity     = CardEntity,
                ScreenPosition = eventData.position,
            });

            // Драг-розыгрыш существа: превью было над полем → отдаём решение системе. Валидная клетка →
            // она коммитит размещение; нет → пришлёт TargetSelectionCancelledEvent (вернём карту штатно).
            if (_creatureDrag)
            {
                _creatureDrag = false;
                _dragToPlace  = false;
                IsSelected    = false;
                _pendingPlay  = true;
                gameObject.SetActive(false);
                return;
            }

            // Существо, отпущенное ВНЕ поля (система сказала «драг-в-поле» этим драгом) → просто возврат
            // в руку. Старый клик-путь (OnUse → PendingSelectCell) для существ больше не включается.
            if (_dragToPlace)
            {
                _dragToPlace = false;
                IsSelected = false;
                SetHighlight(CardHighlightEffect.HighlightType.Selection, false);
                _rectTransform.DOLocalMove(_dragStartLocalPos, _returnDuration)
                    .SetEase(Ease.OutCubic);
                return;
            }

            bool outside = IsOutsideLayout();
            Debug.Log($"[PlayCardView] IsOutsideLayout={outside} layoutRect={_layoutRect}");
            if (outside)
            {
                // Скрываем карту немедленно, розыгрыш или отмена завершат остальное
                IsSelected    = false;
                _pendingPlay  = true;
                gameObject.SetActive(false);
                OnUse();
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

        public override void OnUse()
        {
            Debug.Log($"[PlayCardView] OnActive publishing CardPlayRequestedEvent card={CardEntity}");

            _state.CastEvent(new RequestCardCastEvent
                { 
                    CardEntity = CardEntity 
                }
            ); 
        }

        public void Deselect()
        {
            IsSelected = false;
            SetHighlight(CardHighlightEffect.HighlightType.Selection, false);
        }

        // Растворение карты над полем (dissolve-out). Если на карте есть CardDissolveDriver (UI dissolve-шейдер) —
        // гоним его; иначе базовый фолбэк — альфа CanvasGroup. on=false → вернуть карту видимой.
        private void DissolveCard(bool on)
        {
            var driver = GetComponent<CardDissolveDriver>();
            if (driver != null) { driver.Play(on, 0.25f); return; }
            if (_canvasGroup != null)
            {
                _canvasGroup.DOKill();
                _canvasGroup.DOFade(on ? 0f : 1f, 0.2f);
            }
        }

        private void OnTargetSelectionCancelled(TargetSelectionCancelledEvent evt)
        {
            if (!_pendingPlay || evt.CardEntity != CardEntity) return;
            // Выбор цели отменён — возвращаем карту в руку (и её видимость после dissolve).
            _pendingPlay = false;
            _creatureDrag = false;
            DissolveCard(false);
            if (_canvasGroup != null) { _canvasGroup.DOKill(); _canvasGroup.alpha = 1f; }
            _rectTransform.localPosition = _dragStartLocalPos;
            gameObject.SetActive(true);
            UpdateView();
        }

        private void OnAffordableChanged(CardAffordableChangedEvent evt)
        {
            if (evt.CardEntity != CardEntity) return;
            _isAffordable = evt.IsAffordable;
            SetHighlight(CardHighlightEffect.HighlightType.Affordable, _isAffordable);
        }

        private void OnAbilityReadyChanged(CardAbilityReadyChangedEvent evt)
        {
            if (evt.CardEntity != CardEntity) return;
            _isAbilityReady = evt.IsReady;
            SetHighlight(CardHighlightEffect.HighlightType.AbilityReady, _isAbilityReady);
        }

        private void OnCostChanged(CardCostChangedEvent evt)
        {
            if (evt.CardEntity != CardEntity) return;
            SetCostAmount(evt.EffectiveCost);   // эффективная цена (с модификатором — Гиперинфляция)
        }

        // Живые статы существа в руке (бафф/дебафф/урон командиру): цвет относительно базы + панч.
        // Initial=true — карта только увидена системой (могла прийти уже баффнутой) — без панча.
        private void OnHandStatsChanged(HandCardStatsChangedUIEvent evt)
        {
            if (evt.CardEntity != CardEntity) return;
            SetLiveStats(evt.Attack, evt.MaxHealth, evt.CurrentHealth, punch: !evt.Initial);
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
