using AwesomeUI.Core.Attributes;
using AwesomeUI.Core.Panel;
using DG.Tweening;
using Game.Core.Shared;
using Game.Core.Shared.Interface;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// Заставка перед стартом: название игры + мигающий «Tap to continue». Любой тап/клавиша →
    /// InitState.OnContinue() (стандартный пайплайн: туториал/логин) через [UIInject] IInitStateContext.
    /// Фолбэк без контекста — переход по _nextPanelId. Плюс UnityEvent onContinue.
    ///
    /// isOpenOnInit=FALSE у ВСЕХ панелей LoginCanvas (включая эту) — порядком рулит InitState, открывая
    /// панели явно по id. Заставка показывается ТОЛЬКО когда цикл первого захода пройден целиком
    /// (язык+туториал+ник); во время онбординга её пропускают. Для тапа корню панели нужен
    /// raycast-Graphic (полноэкранный фон); клавиша/любой ввод работают и без него.
    ///
    /// Префаб: _titleText (название — или свой логотип-Image), _tapText («Tap to continue», мигает).
    /// </summary>
    public class TitlePanel : SourcePanel, IPointerClickHandler
    {
        [SerializeField] private TextMeshProUGUI _titleText;
        [SerializeField] private TextMeshProUGUI _tapText;
        [SerializeField] private float _blinkPeriod = 1.1f;

        [Tooltip("ФОЛБЭК: куда перейти по тапу (дропдаун), если нет IInitStateContext. Основной путь — " +
                 "событие OnContinue в InitState (стандартный пайплайн).")]
        [RegistryCategory(RegistryCategories.Panel)]
        [SerializeField] private RegistryId _nextPanelId;

        public UnityEvent onContinue;

        // Основной путь: заставка → InitState.OnContinue() → стандартный пайплайн (туториал/логин).
        [UIInject] private IInitStateContext _initContext;

        bool _continued;
        float _armAt;      // «броня»: не реагируем на ввод сразу после показа (клик, которым жали Play)
        Sequence _blink;

        public override void OnInject()
        {
            base.OnInject();
            if (_titleText != null) _titleText.text = UIStrings.GameTitle;   // «Герои, Злодеи и Ублюдки»
            if (_tapText != null && string.IsNullOrEmpty(_tapText.text))
                _tapText.text = UIStrings.TapToContinue;
        }

        public override void OnOpen(params System.Action[] onComplete)
        {
            base.OnOpen(onComplete);
            _continued = false;
            _armAt = Time.unscaledTime + 0.3f;   // игнорим ввод первые 0.3с
            StartBlink();
        }

        bool Armed => Time.unscaledTime >= _armAt;

        void Update()
        {
            if (isOpen && !_continued && Armed && Input.anyKeyDown) Continue();
        }

        public void OnPointerClick(PointerEventData e) { if (Armed) Continue(); }

        /// <summary>Продолжить (тап/клавиша). Идемпотентно.</summary>
        public void Continue()
        {
            if (_continued) return;
            _continued = true;
            _blink?.Kill();
            if (_initContext != null) _initContext.OnContinue();                     // основной путь
            else if (!_nextPanelId.IsEmpty) _panelController.OpenPanel(_nextPanelId.Value);  // фолбэк по id
            onContinue?.Invoke();
        }

        void StartBlink()
        {
            if (_tapText == null) return;
            var cg = _tapText.GetComponent<CanvasGroup>();
            if (cg == null) cg = _tapText.gameObject.AddComponent<CanvasGroup>();

            _blink?.Kill();
            _blink = DOTween.Sequence().SetUpdate(true);
            _blink.Append(cg.DOFade(0.25f, _blinkPeriod * 0.5f));
            _blink.Append(cg.DOFade(1f, _blinkPeriod * 0.5f));
            _blink.SetLoops(-1);
        }

        public override void Unject() => _blink?.Kill();

        public override void OnDipose()
        {
            _blink?.Kill();
            base.OnDipose();
        }
    }
}
