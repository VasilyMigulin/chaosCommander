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
        [SerializeField] private float _zoomScale = 2.1f;
        [Tooltip("Куда улетает карта при удержании — точка на экране в долях от его размера (0..1, " +
                 "считая от нижнего левого угла). 0.5/0.5 — геометрический центр; по умолчанию чуть выше " +
                 "центра, чтобы не оказаться над нижней панелью руки, откуда карта стартовала.")]
        [SerializeField] private Vector2 _zoomScreenAnchor = new Vector2(0.5f, 0.53f);
        [SerializeField] private float _zoomDuration = 0.22f;
        [Tooltip("Порог сдвига пальца (px экрана) ПОСЛЕ срабатывания зума, после которого предпросмотр " +
                 "сменяется обычным перетаскиванием карты. Меньшие движения зум игнорирует — иначе любое " +
                 "дрожание руки схлопывало бы уже открытый предпросмотр. С учётом того, что карта теперь " +
                 "улетает к центру экрана (далеко от пальца), порог сделан щедрее.")]
        [SerializeField] private float _zoomDragThreshold = 90f;

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
        // Зум ЕЩЁ активен, но Unity уже увидела сдвиг пальца за СВОЙ (маленький) drag-threshold и позвала
        // OnBeginDrag. Реальный драг не стартуем сразу — копим сдвиг САМИ и ждём _zoomDragThreshold
        // (см. OnDrag): так лёгкое дрожание руки не рвёт уже открытый предпросмотр карты.
        private bool          _zoomDragPending;
        private Vector2       _zoomDragStartScreenPos;
        private RectTransform _rectTransform;
        private Canvas        _canvas;
        private RectTransform _layoutRect;  // RectTransform родительского CardLayout
        private CardLayout    _layout;      // сам CardLayout — для передачи ему позы при выходе из зума

        // ── Драг-розыгрыш существа «под пальцем» (мост через bus, UI Mono-кода не знает) ──
        // UI шлёт CreatureDragMoved/Released; CreatureDragPreviewSystem ведёт превью-модель и отвечает
        // CreatureDragOverFieldChangedEvent (карта растворяется/проявляется). Коммит/отмена — на системе.
        private bool _creatureDrag;      // система сказала «карта над полем, превью активно»
        private bool _dragToPlace;       // система сказала «это существо: размещение ТОЛЬКО дропом на поле»

        // ── Zoom-предпросмотр (hold-механика CardBaseView: OnHoldTriggered/OnHoldReleased) ──
        // Работает для ЛЮБОЙ карты руки — доступной и недоступной к розыгрышу (это чтение, не действие).
        private bool    _zoomActive;
        private Vector3 _zoomStartLocalPos;   // база для возврата, если экранную точку не удалось разрешить

        /// <summary>Зумится ли карта прямо сейчас — CardLayout.RefreshFan замораживает её на время
        /// удержания (не трогает позицию/поворот/порядок), пока палец не отпущен.</summary>
        public bool IsZoomed => _zoomActive;

        /// <summary>Тащит ли игрок карту прямо сейчас — та же заморозка в CardLayout.RefreshFan, что и
        /// у зума: позиция карты «под пальцем», а не в слоте веера, второй твин туда — гонка с драгом.</summary>
        public bool IsDragging => _isDragging;

        // Пока карта зумится, потеря наведения — следствие того, что карта САМА улетела из-под пальца
        // к центру экрана, а не что палец соскользнул с неё. Схлопнуть предпросмотр должно только
        // реальное отпускание (OnPointerUp) или переход в настоящий драг (см. CancelZoom в OnDrag).
        protected override bool SuppressExitRelease => _zoomActive;

        /// <summary>Удержание ≥ порога: карта улетает к центру экрана, укрупняется и выходит на первый
        /// план — прочитать текст вдали от пальца, который иначе его же и закрывает. Розыгрыш при этом
        /// не стартует (драг начнётся только с движения пальца, см. _zoomDragThreshold).</summary>
        protected override void OnHoldTriggered()
        {
            if (!IsOccupied || _isDragging || _pendingPlay || _zoomActive) return;

            _zoomActive        = true;
            _zoomStartLocalPos = _rectTransform.localPosition;

            _rectTransform.SetAsLastSibling();   // поверх соседних карт
            _rectTransform.DOKill();
            _rectTransform.DOScale(_zoomScale, _zoomDuration).SetEase(Ease.OutCubic);
            _rectTransform.DOLocalMove(ZoomTargetLocalPos(), _zoomDuration).SetEase(Ease.OutCubic);
            // Карта читается лицом к игроку, а не под своим наклоном в веере — тот наклон нужен только
            // ВНУТРИ строя (см. CardLayout.GetFanPose), у предпросмотра ему взяться неоткуда не должно.
            // Возврат к наклону веера делает CancelZoom (через CardLayout.RefreshFanNow/SnapCardIntoFan).
            _rectTransform.DOLocalRotate(Vector3.zero, _zoomDuration).SetEase(Ease.OutCubic);
        }

        // Точка предпросмотра — экранный центр (см. _zoomScreenAnchor), а не «поднять карту над своим
        // местом»: у краевых карт веера подъём на месте всё равно оставлял карту у края экрана, где
        // половина текста уезжала за рамку/палец. Считаем заново при каждом срабатывании (не кэшируем) —
        // ориентация/разрешение экрана могли смениться между удержаниями.
        private Vector3 ZoomTargetLocalPos() => ScreenPointToLocalPos(
            new Vector2(Screen.width * _zoomScreenAnchor.x, Screen.height * _zoomScreenAnchor.y), _zoomStartLocalPos);

        // Перевод экранной точки в локальные координаты родителя карты — тот же приём, что и у
        // CardLayout.ResolveDealFrom. Общий хелпер: зовёт и ZoomTargetLocalPos (точка предпросмотра),
        // и переход зум→драг в OnDrag (подхват карты под текущим пальцем).
        private Vector3 ScreenPointToLocalPos(Vector2 screenPoint, Vector3 fallback)
        {
            var parent = _rectTransform.parent as RectTransform;
            if (parent == null) return fallback;

            Camera cam = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay ? _canvas.worldCamera : null;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenPoint, cam, out var local)
                ? new Vector3(local.x, local.y, 0f)
                : fallback;
        }

        /// <summary>Палец отпущен/увели после сработавшего удержания — плавно вернуть карту на место.</summary>
        protected override void OnHoldReleased() => CancelZoom(instant: false);

        // Схлопнуть зум и ГАРАНТИРОВАННО вернуть масштаб/позицию/порядок. instant — без твинов
        // (перед стартом драга и на деактивации: DOTween на неактивном объекте не играет).
        //
        // ПОЗИЦИЯ/ПОВОРОТ/ПОРЯДОК НЕ восстанавливаются из снимка на момент зума — этот снимок мог
        // устареть: пока карта держалась, RefreshFan её замораживал (см. CardLayout), но рука могла
        // перестроиться (добор/уход другой карты меняет count → offset/angle ВСЕХ слотов). Снимок
        // тогда указывал бы на СТАРОЕ место — карта визуально проваливалась под уже переехавших
        // соседей и путала свой sibling-индекс (баг 2026-08-04). Вместо этого спрашиваем у CardLayout
        // АКТУАЛЬНУЮ позу. Масштаб — не вееровое свойство, его как был, так и возвращаем сами.
        // repositionToFan=false — старт настоящего драга ПОСЛЕ зума (см. OnDrag): карту подхватываем ровно
        // там, где она сейчас (в центре экрана, куда улетела на удержании), а не телепортируем сперва
        // обратно в слот веера. С «подъёмом на месте» (до 2026-08-17) этот скачок был на десяток пикселей и
        // не читался; теперь карта летит через весь экран — мгновенный прыжок назад «в слот» и обратно под
        // палец выглядел как «дёрнуло на место», хотя это ещё не отпускание, а самое начало перетаскивания.
        private void CancelZoom(bool instant, bool repositionToFan = true)
        {
            _zoomDragPending = false;   // зума больше нет — незачем ждать порог сдвига для несуществующего зума
            if (!_zoomActive) return;
            _zoomActive = false;

            _rectTransform.DOKill();

            if (!repositionToFan)
            {
                _rectTransform.localScale = Vector3.one;   // масштаб — не вееровое свойство, гасим сами
                return;                                     // позицию/поворот НЕ трогаем — драг продолжит отсюда
            }

            if (instant || !gameObject.activeInHierarchy)
            {
                _rectTransform.localScale = Vector3.one;
                // Синхронно: следующая же строка вызывающего (OnBeginDrag) читает localPosition как
                // стартовую точку драга — твин к этому моменту ничего бы ещё не доиграл.
                _layout?.SnapCardIntoFan(this);
                return;
            }

            _rectTransform.DOScale(1f, _zoomDuration).SetEase(Ease.OutCubic);
            _layout?.RefreshFanNow();   // тот же путь и та же анимация, что у любой другой карты веера
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
            _layout        = GetComponentInParent<CardLayout>();
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
            GameEventBus.Subscribe<CardAffectedInHandUIEvent>(OnAffectedInHand);
            GameEventBus.Subscribe<CardTierChangedUIEvent>(OnTierChanged);
            GameEventBus.Subscribe<CardDescriptionChangedUIEvent>(OnDescriptionChanged);
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
            GameEventBus.Unsubscribe<CardAffectedInHandUIEvent>(OnAffectedInHand);
            GameEventBus.Unsubscribe<CardTierChangedUIEvent>(OnTierChanged);
            GameEventBus.Unsubscribe<CardDescriptionChangedUIEvent>(OnDescriptionChanged);
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
        /// Анимация дискарда / самоактивации карты (à la Hearthstone): карта РЫВКОМ приближается к зрителю
        /// (укрупняется + приподнимается), затем ВЫЛЕТАЕТ вверх, ещё увеличиваясь, и растворяется. Раньше она
        /// скукоживалась «в себя» (scale 0.5 + вниз) — плохо считывалось. По завершении вызывает onComplete
        /// (CardLayout очищает слот и пересчитывает веер).
        /// </summary>
        /// <summary>Прилёт карты в руку «как в ХС»: из-за границы экрана по ДУГЕ вверх, укрупняясь;
        /// ЗАВИСАНИЕ крупным планом (игрок успевает прочитать); затем по дуге вниз, уменьшаясь до обычного
        /// размера в свой слот.
        ///
        /// Дуга делается через промежуточную точку: две половины пути с разными ease (OutQuad → InQuad)
        /// дают подъём с замедлением и спуск с ускорением, то есть визуально кривую, а не два отрезка.
        /// from — откуда вылетает (точка добора за краем), to/toAngleZ — место карты в веере (его считает
        /// CardLayout, поэтому карта садится сразу в свой поворот, без доводки вторым твином).
        /// showcase=false — быстрый плоский залёт для раздачи ПАЧКОЙ (стартовая рука): фазы те же, но дуга
        /// низкая и без укрупнения — иначе пять карт зависают крупным планом друг на друге.
        /// onHover зовётся в момент зависания и РЕШАЕТ СУДЬБУ карты: вернул true — карту забрал вызывающий
        /// (у неё эффект «при взятии»: она улетает вперёд), посадки в руку не будет.</summary>
        public void PlayDrawInAnimation(Vector3 from, Vector3 to, float toAngleZ, float duration,
                                        bool showcase = true,
                                        System.Func<bool> onHover = null, System.Action onComplete = null)
        {
            _rectTransform.DOKill();
            _rectTransform.SetAsLastSibling();   // летит поверх соседей

            float rise  = duration * 0.40f;   // из-за края вверх, растёт
            float hold  = duration * 0.22f;   // зависание крупным планом
            float land  = duration - rise - hold;

            float apexRise  = showcase ? 260f : 110f;
            float peakScale = showcase ? 1.5f : 1.05f;

            // Вершина дуги: между точкой вылета и слотом, но заметно ВЫШЕ обеих — отсюда изгиб.
            Vector3 apex = new Vector3((from.x + to.x) * 0.5f, Mathf.Max(from.y, to.y) + apexRise, 0f);

            _rectTransform.localPosition = from;
            _rectTransform.localScale    = Vector3.one * 0.55f;
            _rectTransform.localRotation = Quaternion.identity;

            // Подъём и зависание — ОТДЕЛЬНОЙ последовательностью: посадку добавляем только после того, как
            // onHover сказал, что карта вообще садится. Одной готовой Sequence так нельзя — ветка решается
            // в полёте, а обрывать уже играющую цепочку (DOKill изнутри её же колбэка) — напрашиваться на гонку.
            // Наклон в подъёме сознательно убран (был -4°): карту, которую забирают на зависании
            // (форс-каст/деколёт-показ — onHover()==true), никто не довернёт обратно — посадки не будет,
            // и она висела бы криво весь hold + показ (до ~0.8с, заметно). Читаемость движения даёт дуга
            // и рост масштаба; поворот остаётся только на посадке (PlayDrawInLanding), где есть кому его снять.
            var seq = DOTween.Sequence();
            seq.Append(_rectTransform.DOLocalMove(apex, rise).SetEase(Ease.OutQuad));
            seq.Join(_rectTransform.DOScale(peakScale, rise).SetEase(Ease.OutCubic));
            seq.AppendInterval(hold);
            seq.OnComplete(() =>
            {
                if (onHover != null && onHover()) return;   // карту забрали на зависании — в руку она не сядет
                PlayDrawInLanding(to, toAngleZ, land, onComplete);
            });
        }

        // Спуск по дуге в свой слот веера: уменьшение до обычного размера + доворот в угол веера.
        private void PlayDrawInLanding(Vector3 to, float toAngleZ, float duration, System.Action onComplete)
        {
            var seq = DOTween.Sequence();
            seq.Append(_rectTransform.DOLocalMove(to, duration).SetEase(Ease.InQuad));
            seq.Join(_rectTransform.DOScale(1f, duration).SetEase(Ease.InCubic));
            seq.Join(_rectTransform.DOLocalRotate(new Vector3(0f, 0f, toAngleZ), duration).SetEase(Ease.InQuad));
            seq.OnComplete(() =>
            {
                _rectTransform.localPosition = to;
                _rectTransform.localScale    = Vector3.one;
                _rectTransform.localRotation = Quaternion.Euler(0f, 0f, toAngleZ);
                onComplete?.Invoke();
            });
        }

        /// <summary>Замешивание в колоду: та же дуговая механика, что у PlayDrawInAnimation (два
        /// DOLocalMove с Out/In-изингом сшиваются в одну плавную кривую через промежуточную точку), но
        /// прогнутая ВНИЗ, а не вверх — карта ныряет, а не взлетает. 1) Уход вниз-в-сторону до точки
        /// зависания + рост крупным планом. 2) Зависание — читаемая пауза (holdDuration), как у форс-каста
        /// добора (CardLayout.ShowcaseTail._autoPlayHoldDuration) — дать игроку прочитать, ЧТО именно
        /// замешивается. 3) Уход в offscreen-точку колоды (реверс направления добора — «ушло туда, откуда
        /// приходят карты»), схлопывание (уменьшение + фейд) вместо посадки в слот.
        /// from — точка появления (обычно позиция источника эффекта), to — offscreen-точка добора
        /// (_dealInOffset у CardLayout).</summary>
        public void PlayShuffleToDeckAnimation(Vector3 from, Vector3 to, float riseDuration, float holdDuration, float awayDuration, System.Action onComplete = null)
        {
            _rectTransform.DOKill();
            _rectTransform.SetAsLastSibling();
            if (_canvasGroup != null) _canvasGroup.blocksRaycasts = false;

            // Прогиб ВНИЗ (в отличие от PlayDrawInAnimation, где апекс выше обеих точек) — ниже обеих точек.
            Vector3 dip = new Vector3((from.x + to.x) * 0.5f, Mathf.Min(from.y, to.y) - 180f, 0f);

            _rectTransform.localPosition = from;
            _rectTransform.localScale    = Vector3.one * 0.55f;
            _rectTransform.localRotation = Quaternion.identity;
            if (_canvasGroup != null) _canvasGroup.alpha = 1f;

            var seq = DOTween.Sequence();
            seq.Append(_rectTransform.DOLocalMove(dip, riseDuration).SetEase(Ease.OutQuad));
            seq.Join(_rectTransform.DOScale(1.5f, riseDuration).SetEase(Ease.OutCubic));
            seq.AppendInterval(holdDuration);
            seq.Append(_rectTransform.DOLocalMove(to, awayDuration).SetEase(Ease.InQuad));
            seq.Join(_rectTransform.DOScale(0.2f, awayDuration).SetEase(Ease.InCubic));
            if (_canvasGroup != null) seq.Join(_canvasGroup.DOFade(0f, awayDuration).SetEase(Ease.InCubic));
            seq.OnComplete(() =>
            {
                if (_canvasGroup != null) { _canvasGroup.alpha = 1f; _canvasGroup.blocksRaycasts = true; }
                _rectTransform.localScale    = Vector3.one;
                _rectTransform.localRotation = Quaternion.identity;
                onComplete?.Invoke();
            });
        }

        public void PlayDiscardAnimation(System.Action onComplete, float duration = 1f)
        {
            _zoomActive = false;   // дискард (в т.ч. эффектом оппонента ПОКА карту держат) главнее зума
            _rectTransform.DOKill();
            _rectTransform.SetAsLastSibling();   // «вылетает» поверх соседних карт
            if (_canvasGroup != null) _canvasGroup.blocksRaycasts = false;

            Vector3 startPos = _rectTransform.localPosition;
            float pop  = duration * 0.34f;   // рывок ВПЕРЁД, к зрителю
            float away = duration - pop;     // продолжает лететь вперёд и растворяется

            // Масштаб берём ОТ ТЕКУЩЕГО, а не от константы: карту могли отдать сюда прямо с зависания дуги
            // добора (она уже крупная) — тогда фиксированные 1.45 читались бы как рывок НАЗАД перед вылетом.
            float from = _rectTransform.localScale.x;
            float popScale  = Mathf.Max(1.45f, from * 1.08f);
            float awayScale = Mathf.Max(2.1f,  popScale * 1.45f);

            // «Вперёд» в UI-канвасе = к зрителю: карта РАСТЁТ и одновременно идёт к ЦЕНТРУ экрана.
            // Раньше она росла, но уезжала строго вверх (X не менялся) — движение читалось как «улетела
            // наверх», а не «полетела на игрока». Гасим X к нулю (центр CardLayout) — тогда рост и
            // траектория складываются в один жест «карта надвигается».
            var seq = DOTween.Sequence();
            // фаза 1 — рывок к зрителю: заметный рост + сдвиг к центру
            seq.Append(_rectTransform.DOScale(popScale, pop).SetEase(Ease.OutBack));
            seq.Join(_rectTransform.DOLocalMove(new Vector3(startPos.x * 0.45f, startPos.y + 90f, 0f), pop).SetEase(Ease.OutCubic));
            seq.Join(_rectTransform.DOLocalRotate(new Vector3(0f, 0f, 5f), pop).SetEase(Ease.OutCubic));
            // фаза 2 — надвигается дальше (ещё крупнее, ещё ближе к центру) и растворяется
            seq.Append(_rectTransform.DOScale(awayScale, away).SetEase(Ease.InCubic));
            seq.Join(_rectTransform.DOLocalMove(new Vector3(0f, startPos.y + 190f, 0f), away).SetEase(Ease.InCubic));
            seq.Join(_rectTransform.DOLocalRotate(new Vector3(0f, 0f, 10f), away).SetEase(Ease.InCubic));
            if (_canvasGroup != null) seq.Insert(pop, _canvasGroup.DOFade(0f, away).SetEase(Ease.InCubic));
            seq.OnComplete(() =>
            {
                if (_canvasGroup != null) _canvasGroup.alpha = 1f;
                _rectTransform.localScale    = Vector3.one;
                _rectTransform.localRotation = Quaternion.identity;
                _rectTransform.localPosition = startPos;   // слот переиспользуется — вернуть трансформ к базе
                onComplete?.Invoke();
            });
        }

        /// <summary>Очистить View и деактивировать для повторного использования. <paramref name="keepEntity"/> —
        /// НЕ сбрасывать CardEntity: нужно командиру, чей UI-слот прячется, пока он на столе (ClearCard тут не
        /// «отдать слот другой карте», а «временно спрятать СВОЮ же»), а тем временем ECS-события на его
        /// entity (напр. CommanderOnCooldownUIEvent при возврате) продолжают приходить и матчатся по CardEntity
        /// независимо от того, вызвал ли CardLayout.SetCard уже свой обратный показ — раньше сброс в -1 ронял
        /// такое событие НАВСЕГДА (оно одноразовое, не повторяется), если оно успевало прийти раньше показа.</summary>
        public virtual void ClearCard(bool keepEntity = false)
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
            if (!keepEntity) CardEntity = -1;
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
            Debug.Log($"[PlayCardView] OnBeginDrag card={CardEntity} occupied={IsOccupied} affordable={_isAffordable} zoomed={_zoomActive}");

            // Зум ЕЩЁ активен — Unity увидела сдвиг за СВОЙ (маленький, единицы пикселей) drag-threshold,
            // но рвать уже открытый предпросмотр от такого дрожания руки не нужно. НЕ стартуем драг здесь:
            // копим сдвиг с этой точки сами и ждём куда более заметного _zoomDragThreshold в OnDrag.
            if (_zoomActive)
            {
                _zoomDragPending        = true;
                _zoomDragStartScreenPos = eventData.position;
                return;
            }

            // Драг стартует не из зума (обычный, «сходу») — зум-предпросмотра тут и не было, но вызов
            // безвреден (CancelZoom сам проверяет _zoomActive).
            CancelZoom(instant: true);

            if (!IsOccupied || !_isAffordable) return;

            StartRealDrag();
        }

        // Настоящий старт перетаскивания — вынесен отдельно: тот же код нужен и «сходу» (обычный драг из
        // покоя), и когда порог _zoomDragThreshold пройден ПОСЛЕ уже открытого зума (см. OnDrag).
        private void StartRealDrag()
        {
            _isDragging            = true;
            _creatureDrag          = false;
            _dragToPlace           = false;   // система скажет заново для ЭТОГО драга (CreatureDragStartedEvent)
            _dragStartLocalPos     = _rectTransform.localPosition;
            _dragStartSiblingIndex = _rectTransform.GetSiblingIndex();

            _rectTransform.DOKill();

            IsSelected = true;
            SetHighlight(CardHighlightEffect.HighlightType.Selection, true);
            GameEventBus.Publish(new CardDragTargetPreviewEvent { CardEntity = CardEntity, Active = true });

            // Карта поверх остальных во время перетаскивания
            _rectTransform.SetAsLastSibling();

            // Не блокируем рейкасты во время перетаскивания
            _canvasGroup.blocksRaycasts = false;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_zoomDragPending)
            {
                // Ждём, пока сдвиг ОТ ТОЧКИ, где палец был при срабатывании зума, не перерастёт в
                // настоящее перетаскивание. Пока порог не пройден — карта висит зумнутой на месте.
                if (Vector2.Distance(eventData.position, _zoomDragStartScreenPos) < _zoomDragThreshold) return;

                _zoomDragPending = false;

                if (!IsOccupied || !_isAffordable)
                {
                    // Недоступна — драга не будет, ведём как обычное отпускание (плавный возврат в слот
                    // веера). ВАЖНО звать именно так (repositionToFan остаётся true по умолчанию) — раньше
                    // тут снимали зум БЕЗ возврата в слот и без старта драга, и карта застревала «сжатой»
                    // на месте, где её застал порог (баг юзера, скрин: карта висит уменьшенная и мёртвая).
                    CancelZoom(instant: false);
                    return;
                }

                // Подхватываем карту под текущим пальцем (а не оставляем висеть в старой зум-точке — это
                // уже отдельный кадр с новой позицией пальца) и сразу готовим её к перетаскиванию.
                CancelZoom(instant: true, repositionToFan: false);
                _rectTransform.localPosition = ScreenPointToLocalPos(eventData.position, _rectTransform.localPosition);
                StartRealDrag();
                // Эту дельту (уже потраченную на «примерку» порога) не проматываем в позицию — драг
                // просто продолжит со СЛЕДУЮЩЕГО кадра от текущей (уже верной) точки.
            }

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
            _zoomDragPending = false;   // жест закончился ДО порога зум→драг — дальше нечего ждать

            if (!_isDragging) return;   // порог так и не был пройден (или это не наш драг вовсе)
            _isDragging = false;
            _canvasGroup.blocksRaycasts = true;

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
                GameEventBus.Publish(new CardDragTargetPreviewEvent { CardEntity = CardEntity, Active = false });
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
                GameEventBus.Publish(new CardDragTargetPreviewEvent { CardEntity = CardEntity, Active = false });
                ReturnToFanSlot();
                return;
            }

            bool outside = IsOutsideLayout();
            Debug.Log($"[PlayCardView] IsOutsideLayout={outside} layoutRect={_layoutRect}");
            if (outside)
            {
                // Скрываем карту немедленно, розыгрыш или отмена завершат остальное
                IsSelected    = false;
                GameEventBus.Publish(new CardDragTargetPreviewEvent { CardEntity = CardEntity, Active = false });
                _pendingPlay  = true;
                gameObject.SetActive(false);
                OnUse();
            }
            else
            {
                // Возврат на место
                IsSelected = false;
                SetHighlight(CardHighlightEffect.HighlightType.Selection, false);
                GameEventBus.Publish(new CardDragTargetPreviewEvent { CardEntity = CardEntity, Active = false });
                ReturnToFanSlot();
            }
        }

        // Плавный возврат в АКТУАЛЬНЫЙ слот веера — НЕ туда, где карта стояла на СТАРТЕ драга: рука
        // могла перестроиться, пока карту тащили (добор/уход другой карты меняют count → offset/angle
        // ВСЕХ слотов), и снятая на старте `_dragStartLocalPos`/`_dragStartSiblingIndex` тогда указывали
        // бы на СТАРОЕ место — тот же класс бага, что чинили для зум-удержания (см. CardLayout.RefreshFanNow,
        // 2026-08-04). Один вызов чинит и позицию/поворот, и порядок в иерархии — сразу всем картам веера.
        private void ReturnToFanSlot()
        {
            if (_layout != null) { _layout.RefreshFanNow(); return; }

            // Фолбэк вне CardLayout (в этом проекте не встречается — SimpleCardView/CommanderCardView
            // живут только внутри него, — но не падать же молча, если контекст всё-таки другой).
            _rectTransform.SetSiblingIndex(_dragStartSiblingIndex);
            _rectTransform.DOLocalMove(_dragStartLocalPos, _returnDuration).SetEase(Ease.OutCubic);
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
            GameEventBus.Publish(new CardDragTargetPreviewEvent { CardEntity = CardEntity, Active = false });
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

            // АКТУАЛЬНАЯ поза, а не та, что была на старте драга: рука могла перестроиться, пока окно
            // выбора цели висело (добор/уход другой карты меняет count → offset/angle ВСЕХ слотов) —
            // тот же класс бага, что чинили для зум-удержания/драга (см. CardLayout.SnapCardIntoFan).
            if (_layout != null) _layout.SnapCardIntoFan(this);
            else _rectTransform.localPosition = _dragStartLocalPos;   // фолбэк вне CardLayout

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
            // ВРЕМЕННО (баг: не подсвечивается рыжим) — видим ВСЕ входящие события и матч по CardEntity.
            UnityEngine.Debug.Log($"[CondHighlight] PlayCardView.OnAbilityReadyChanged evt.CardEntity={evt.CardEntity} this.CardEntity={CardEntity} ready={evt.IsReady} match={evt.CardEntity == CardEntity}");
            if (evt.CardEntity != CardEntity) return;
            _isAbilityReady = evt.IsReady;
            SetHighlight(CardHighlightEffect.HighlightType.AbilityReady, _isAbilityReady);
        }

        private void OnCostChanged(CardCostChangedEvent evt)
        {
            if (evt.CardEntity != CardEntity) return;
            SetCostAmount(evt.EffectiveCost);      // эффективная цена (с модификатором — Гиперинфляция)
            SetAltCostIcon(evt.AltCostKind);       // иконка уплаты: маркер Букмекера ↔ штатный ресурс
        }

        // Живые статы существа в руке (бафф/дебафф/урон командиру): цвет относительно базы + панч.
        // Initial=true — карта только увидена системой (могла прийти уже баффнутой) — без панча.
        private void OnHandStatsChanged(HandCardStatsChangedUIEvent evt)
        {
            if (evt.CardEntity != CardEntity) return;
            SetLiveStats(evt.Attack, evt.MaxHealth, evt.CurrentHealth, evt.Speed, punch: !evt.Initial);
        }

        // К этой карте руки применили эффект (Дупликатор скопировал / удешевили / баффнули) — punch + VFX.
        private void OnAffectedInHand(CardAffectedInHandUIEvent evt)
        {
            if (evt.CardEntity != CardEntity) return;
            PlayAffectFeedback(evt.Kind);
        }

        // Карта-с-уровнями сменила уровень (или первый показ) — баннер уровня + перерендеренное описание.
        private void OnTierChanged(CardTierChangedUIEvent evt)
        {
            if (evt.CardEntity != CardEntity) return;
            SetTierBanner(evt.Tier + 1);   // 1-based для игрока
            if (!string.IsNullOrEmpty(evt.Description)) SetDescriptionLive(evt.Description);
        }

        // Живой перерендер описания без баннера (Зачарованный: превью «N+бонус» у чары в руке).
        private void OnDescriptionChanged(CardDescriptionChangedUIEvent evt)
        {
            if (evt.CardEntity != CardEntity) return;
            if (!string.IsNullOrEmpty(evt.Description)) SetDescriptionLive(evt.Description);
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
