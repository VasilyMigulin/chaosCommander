using AwesomeUI.Core.Events;
using AwesomeUI.Core.Window;
using AwesomeUI.Core.Layout;
using System;
using System.Collections.Generic;
using UnityEngine; 
using UnityEngine.Events;
using DG.Tweening;
using AwesomeUI.Interface;

namespace AwesomeUI.Core.Panel
{
    [RequireComponent(typeof(CanvasGroup))]
    public abstract class SourcePanel : MonoBehaviour, IPanel
    {
        [Header("Events")]
        public UnityEvent OnOpenEvent;
        public UnityEvent OnCloseEvent;

        [Header("Animation")]
        public Ease EaseOpen = Ease.InBack;
        public Ease EaseClose = Ease.OutBack;
        public bool IsScaleAnimation;
        public bool IsFadeAnimation = true;
        public float DurationAnimate = 0.3f;

        [Header("Panel Settings")]
        public bool isOpenOnInit;
        public bool isAlwaysOpen;
        public bool isOpen => _isOpen;
        protected bool _isOpen;

        [Tooltip("Id панели для навигации ПО ID из AwesomeButton (префабы панели и кнопки не связаны " +
                 "прямой ссылкой). Пусто → берётся имя класса панели.")]
        [PanelId]
        [SerializeField] private string _panelId;
        /// <summary>Идентификатор панели для навигации по id. По умолчанию — имя типа (класса).</summary>
        public string PanelId => string.IsNullOrEmpty(_panelId) ? GetType().Name : _panelId;

        protected List<SourceWindow> _windows;
        protected List<SourceLayout> _layouts;
        protected CanvasGroup _canvasGroup; 
        protected RectTransform _rectTransform;
        protected Sequence _sequenceHide;
        protected Sequence _sequenceShow;
        protected Action[] _callbacks = Array.Empty<Action>();

        protected IPanelController _panelController;

        public virtual void Init(IPanelController panelController)
        {
            _panelController = panelController;
            _rectTransform = GetComponent<RectTransform>();
            _canvasGroup = GetComponent<CanvasGroup>();
            _windows = new List<SourceWindow>();
            _layouts = new List<SourceLayout>(); 

            var windows = GetComponentsInChildren<SourceWindow>(true);
            var layouts = GetComponentsInChildren<SourceLayout>(true);

            foreach (var window in windows)
                _windows.Add(window.Init());

            foreach (var layout in layouts)
                _layouts.Add(layout.Init());

            _isOpen = false;

            if (isOpenOnInit)
                OnOpen();
            else
                gameObject.SetActive(false);
        }

        public abstract void Unject();
        public virtual void OnInject() { }

        public virtual void OnOpen(params Action[] onComplete)
        {
            gameObject.SetActive(true);

            // Кнопки этой панели, что открывают ДРУГИЕ панели, могут вести в раздел, которого нет в
            // текущей сцене/сборке (недособранный черновой раздел меню) — гасим их автоматически при
            // каждом открытии панели, не дожидаясь клика (см. GatePanelButton).
            RefreshPanelGates();

            if (_isOpen)
                return;

            Show(onComplete);
        }

        /// <summary>Переопределяется в наследнике: перечислить GatePanelButton&lt;T&gt;(button) для каждой
        /// своей кнопки, ведущей на другую панель. Вызывается автоматически на каждый OnOpen() — если
        /// целевая панель недоступна (не собрана в этой сцене), кнопка сама выключится.</summary>
        protected virtual void RefreshPanelGates() { }

        /// <summary>Включает/выключает button по факту наличия панели типа T у контроллера (GetPanel&lt;T&gt;).
        /// Ничего не открывает — только гейтит interactable. Null-safe и на button, и на панель.</summary>
        protected void GatePanelButton<T>(UnityEngine.UI.Button button) where T : class, IPanel
        {
            if (button == null) return;
            button.interactable = _panelController != null && _panelController.TryGetPanel<T>(out _);
        }

        public virtual void OnCLose(params Action[] onComplete)
        {
            if (_isOpen)
                Hide(onComplete);
            else
                gameObject.SetActive(false);
        }

        protected virtual void Show(params Action[] onComplete)
        {
            _sequenceHide?.Kill();

            gameObject.SetActive(true);
            _canvasGroup.alpha = IsFadeAnimation ? 0f : 1f;

            if (IsScaleAnimation)
                _rectTransform.localScale = Vector3.one * 0.9f;

            _sequenceShow = DOTween.Sequence();
            _sequenceShow.SetUpdate(UpdateType.Normal, true);

            if (IsFadeAnimation)
                _sequenceShow.Append(_canvasGroup.DOFade(1f, DurationAnimate));

            if (IsScaleAnimation)
                _sequenceShow.Join(_rectTransform.DOScale(1f, DurationAnimate).SetEase(EaseOpen));

            _sequenceShow.OnComplete(() =>
            {
                foreach (var action in _callbacks)
                    action?.Invoke();

                foreach (var action in onComplete)
                    action?.Invoke();

                OnOpenEvent?.Invoke();
                _isOpen = true;

                UIEventBus.Publish(new PanelOpenedEvent { PanelType = GetType(), Panel = this });
            });
        }

        protected virtual void Hide(params Action[] onComplete)
        {
            _sequenceShow?.Kill();

            _sequenceHide = DOTween.Sequence();
            _sequenceHide.SetUpdate(UpdateType.Normal, true);

            if (IsFadeAnimation)
                _sequenceHide.Append(_canvasGroup.DOFade(0f, DurationAnimate));

            if (IsScaleAnimation)
                _sequenceHide.Join(_rectTransform.DOScale(0.9f, DurationAnimate).SetEase(EaseClose));

            _sequenceHide.OnComplete(() =>
            {
                foreach (var action in onComplete)
                    action?.Invoke();

                OnCloseEvent?.Invoke();
                gameObject.SetActive(false);
                _isOpen = false;

                UIEventBus.Publish(new PanelClosedEvent { PanelType = GetType(), Panel = this });
            });
        }

        public virtual T OpenWindow<T>() where T : SourceWindow
        {
            SourceWindow target = null;
            foreach (var window in _windows)
            {
                if (window is T matched)
                    target = matched;
                else
                    window.OnClose();
            }

            target?.OnOpen();
            return target as T;
        }

        public virtual T CloseWindow<T>() where T : SourceWindow
        {
            foreach (var window in _windows)
            {
                if (window is T matched)
                {
                    matched.OnClose();
                    return matched;
                }
            }
            return null;
        }

        public virtual T GetLayout<T>() where T : SourceLayout
        {
            foreach (var layout in _layouts)
            {
                if (layout is T matched)
                    return matched;
            }
            return null;
        }

        public virtual T OpenLayout<T>() where T : SourceLayout
        {
            SourceLayout target = null;
            foreach (var layout in _layouts)
            {
                if (layout is T matched)
                    target = matched;
                else
                    layout.OnClose();
            }

            target?.OnOpen();
            return target as T;
        }

        public virtual T CloseLayout<T>() where T : SourceLayout
        {
            foreach (var layout in _layouts)
            {
                if (layout is T matched)
                {
                    matched.OnClose();
                    return matched;
                }
            }
            return null;
        }

        public virtual void OnDipose()
        {
            foreach (var layout in _layouts)
                layout.Dispose();

            foreach (var window in _windows)
                window.Dispose();

            _sequenceHide?.Kill();
            _sequenceShow?.Kill();

            UIEventBus.UnsubscribeAll(this);
        }
    }
}