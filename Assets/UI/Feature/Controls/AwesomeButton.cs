using DG.Tweening;
using AwesomeUI.Interface;
using Game.Core.Shared;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// Единый функциональный компонент-обёртка для кнопок AwesomeUI. Один MonoBehaviour = всё сразу:
    ///
    /// 1) АНИМАЦИЯ нажатия — press (вдав) / hover (десктоп) / release (пружина), DOTween, unscaled-время.
    /// 2) НАВИГАЦИЯ ПАНЕЛЕЙ ПО ID — укажи id целевой панели (_targetPanelId, дропдаун) в инспекторе:
    ///    клик сам вызовет controller.OpenPanel(id). НИКАКИХ прямых ссылок на префаб панели — кнопка
    ///    и панель живут в разных префабах, связаны только строковым id (SourcePanel.PanelId).
    ///    Контроллер берётся из родительского канваса (GetComponentInParent&lt;IPanelController&gt;).
    /// 3) «НАЗАД» — поставь _back=true, клик вызовет controller.Back() (журнал истории в SourceCanvas).
    /// 4) ВЫБРАННОЕ СОСТОЯНИЕ (табы) — если _syncSelected, кнопка сама подсвечивается, пока её панель
    ///    открыта (резолвит панель по id и поллит isOpen): красит _tintTargets и показывает _indicator.
    /// 5) САМОГЕЙТ — если панели с таким id нет в сцене, навигационная кнопка (_hideIfNoTarget) прячется.
    ///
    /// Работает и как «просто кнопка»: без _targetPanelId/_back — дергай через onClick (UnityEvent).
    /// Unity Button добавляется автоматически (RequireComponent) — берём с него клик/interactable.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Button))]
    public class AwesomeButton : MonoBehaviour,
        IPointerClickHandler, IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler
    {
        [Header("Навигация (опц.)")]
        [Tooltip("Id целевой панели (дропдаун из каталога, категория UI/Panel). Открывается ПО ID через " +
                 "контроллер — префабы кнопки и панели не связаны прямой ссылкой. Пусто → только onClick.")]
        [RegistryCategory(RegistryCategories.Panel)]
        [SerializeField] private RegistryId _targetPanelId;
        [Tooltip("Кнопка «назад»: клик вызывает Back() вместо открытия панели.")]
        [SerializeField] private bool _back;

        /// <summary>Кнопка настроена как навигационная (ведёт «назад» или открывает панель) — такую
        /// НЕЛЬЗЯ вешать на действия вроде выхода из приложения (см. MenuHudPanel.OnInject).</summary>
        public bool IsNavigation => _back || !_targetPanelId.IsEmpty;
        [Tooltip("Спрятать кнопку, если целевая панель не задана/отсутствует в сцене. Включай для " +
                 "ТАБ-кнопок навигации (авто-гейт разделов). Для action-кнопок (Выход и т.п.) — выкл.")]
        [SerializeField] private bool _hideIfNoTarget = false;

        [Header("Действие")]
        public UnityEvent onClick;

        [Header("Анимация нажатия")]
        [Tooltip("Что масштабировать. Пусто → сам объект.")]
        [SerializeField] private RectTransform _scaleTarget;
        [SerializeField] private float _pressScale = 0.92f;
        [SerializeField] private float _hoverScale = 1.04f;
        [SerializeField] private float _pressDur = 0.08f;
        [SerializeField] private float _releaseDur = 0.20f;

        [Header("Выбранное состояние (для табов навигации)")]
        [Tooltip("Синхронизировать подсветку с открытием целевой панели.")]
        [SerializeField] private bool _syncSelected = true;
        [SerializeField] private Graphic[] _tintTargets;
        [SerializeField] private Color _normalColor = new Color(1f, 1f, 1f, 0.6f);
        [SerializeField] private Color _selectedColor = Color.white;
        [Tooltip("Индикатор выбранной вкладки (подчёркивание/точка). Появляется с анимацией.")]
        [SerializeField] private GameObject _indicator;
        [SerializeField] private float _tintDur = 0.20f;

        [Header("Бейдж «новое» (опц.)")]
        [Tooltip("Индикатор новинок (красная точка/бейдж). Управляется NotifyState по ключу.")]
        [SerializeField] private GameObject _notifyBadge;
        [Tooltip("Ключ раздела в NotifyState (см. NotifyKeys). Клик по кнопке гасит бейдж.")]
        [RegistryCategory(RegistryCategories.Notify)]
        [SerializeField] private RegistryId _notifyKey;

        IPanelController _controller;
        IPanel _resolvedTarget;
        Button _button;
        Selectable _selectable;
        Tween _scaleTween;
        bool _pressed, _hover, _selected, _hasButton, _gated;

        void Awake()
        {
            if (_scaleTarget == null) _scaleTarget = transform as RectTransform;
            _button = GetComponent<Button>();
            _selectable = _button != null ? _button : GetComponent<Selectable>();
            _hasButton = _button != null;
            _controller = GetComponentInParent<IPanelController>(true);   // includeInactive!
        }

        // Ленивый резолв контроллера (канваса): в Awake родитель мог быть неактивен/не собран.
        IPanelController Controller
        {
            get
            {
                if (_controller == null) _controller = GetComponentInParent<IPanelController>(true);
                return _controller;
            }
        }

        void OnEnable()
        {
            ApplySelected(false, animate: false);
            NotifyState.Changed += OnNotifyChanged;
            RefreshBadge();
        }

        void OnDisable()
        {
            NotifyState.Changed -= OnNotifyChanged;
            _scaleTween?.Kill();
            _pressed = _hover = false;
            if (_scaleTarget != null) _scaleTarget.localScale = Vector3.one;
        }


        bool Interactable => _selectable == null || _selectable.interactable;

        // ── Клик ──────────────────────────────────────────────────────────────
        // Клик ловим САМИ (IPointerClickHandler), не завися от Unity Button.onClick/interactable.
        public void OnPointerClick(PointerEventData e) => Fire();

        void Fire()
        {
            if (!Interactable)
            {
                Debug.LogWarning($"[AwesomeButton] '{name}': Interactable=FALSE (у Unity Button снят Interactable) — навигация отменена.", this);
                return;
            }

            var ctrl = Controller;
            if (_back) { ctrl?.Back(); }
            else if (!_targetPanelId.IsEmpty)
            {
                if (ctrl == null)
                    Debug.LogWarning($"[AwesomeButton] '{name}': не найден IPanelController (канвас).", this);
                else if (ctrl.OpenPanel(_targetPanelId.Value) == null)
                    Debug.LogWarning($"[AwesomeButton] '{name}': панель с id '{_targetPanelId.Value}' не найдена в канвасе (проверь PanelId).", this);
            }

            if (!_notifyKey.IsEmpty) NotifyState.Clear(_notifyKey);   // раздел открыт → «просмотрено»
            onClick?.Invoke();
        }

        // ── Бейдж «новое» ──────────────────────────────────────────────────────
        void OnNotifyChanged(RegistryId key, bool on) { if (key == _notifyKey) SetBadge(on); }
        void RefreshBadge() { if (_notifyBadge != null && !_notifyKey.IsEmpty) SetBadge(NotifyState.Has(_notifyKey)); }
        void SetBadge(bool on) { if (_notifyBadge != null) _notifyBadge.SetActive(on); }
        /// <summary>Ручное управление бейджем (если не через NotifyState).</summary>
        public void SetNotify(bool on) => SetBadge(on);

        // ── Анимация ──────────────────────────────────────────────────────────
        public void OnPointerDown(PointerEventData e) { if (Interactable) { _pressed = true; Scale(_pressScale, _pressDur, Ease.OutQuad); } }
        public void OnPointerUp(PointerEventData e)    { _pressed = false; Scale(_hover ? _hoverScale : 1f, _releaseDur, Ease.OutBack); }
        public void OnPointerEnter(PointerEventData e) { _hover = true;  if (!_pressed && Interactable) Scale(_hoverScale, _releaseDur, Ease.OutQuad); }
        public void OnPointerExit(PointerEventData e)  { _hover = false; if (!_pressed) Scale(1f, _releaseDur, Ease.OutQuad); }

        void Scale(float scale, float dur, Ease ease)
        {
            if (_scaleTarget == null) return;
            _scaleTween?.Kill();
            _scaleTween = _scaleTarget.DOScale(scale, dur).SetEase(ease).SetUpdate(true);
        }

        // ── Выбранное состояние (резолв панели по id + поллинг isOpen) ─────────
        void Update()
        {
            // Отложенный самогейт: как только контроллер готов — прячем таб, если панели с id нет в сцене.
            if (!_gated && _hideIfNoTarget && !_back && Controller != null)
            {
                _gated = true;
                if (_targetPanelId.IsEmpty || Target == null) { gameObject.SetActive(false); return; }
            }

            if (!_syncSelected) return;
            var t = Target;
            if (t == null) return;
            bool on = t.isOpen;
            if (on != _selected) ApplySelected(on, animate: true);
        }

        // Резолвим целевую панель по id ОДИН раз (кэш) через контроллер канваса.
        IPanel Target
        {
            get
            {
                if (_resolvedTarget == null && !_targetPanelId.IsEmpty)
                    _resolvedTarget = Controller?.GetPanel(_targetPanelId.Value);
                return _resolvedTarget;
            }
        }

        /// <summary>Ручное управление подсветкой (если не хочешь автосинк).</summary>
        public void SetSelected(bool on, bool animate = true) => ApplySelected(on, animate);

        void ApplySelected(bool on, bool animate)
        {
            _selected = on;

            if (_tintTargets != null)
                foreach (var g in _tintTargets)
                {
                    if (g == null) continue;
                    if (animate) g.DOColor(on ? _selectedColor : _normalColor, _tintDur).SetUpdate(true);
                    else g.color = on ? _selectedColor : _normalColor;
                }

            if (_indicator != null)
            {
                var rt = _indicator.transform as RectTransform;
                if (on)
                {
                    _indicator.SetActive(true);
                    if (animate && rt != null) { rt.localScale = Vector3.zero; rt.DOScale(1f, _tintDur).SetEase(Ease.OutBack).SetUpdate(true); }
                }
                else if (animate && rt != null)
                {
                    rt.DOScale(0f, _tintDur * 0.7f).SetUpdate(true).OnComplete(() => _indicator.SetActive(false));
                }
                else _indicator.SetActive(false);
            }
        }
    }
}
