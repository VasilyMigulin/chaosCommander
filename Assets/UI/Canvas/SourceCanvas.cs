using AwesomeUI.Core.Attributes;
using AwesomeUI.Interface;
using AwesomeUI.Core.Events;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace AwesomeUI.Core.Canvas
{
    public abstract class SourceCanvas : MonoBehaviour, IPanelController
    {
        protected UnityEngine.Canvas _canvas;
        protected List<Panel.SourcePanel> _panels;
        public bool IsInited { get; protected set; }

        // Журнал истории навигации (для жеста/кнопки «назад»). isAlwaysOpen-панели сюда НЕ попадают.
        readonly Stack<IPanel> _history = new Stack<IPanel>();
        IPanel _current;

        /// <summary>Канвас активен (виден) — по нему маршрутизируем «назад» на активный экран.</summary>
        public bool IsOpen => _canvas != null && _canvas.enabled;
        public bool CanGoBack => _history.Count > 0;

        [Header("Canvas Settings")]
        public bool IsOpenOnStart;
        public bool IsPersistent; // Не закрывать при переключении на другой Canvas

        public virtual void Init()
        {
            _canvas = GetComponent<UnityEngine.Canvas>();
            _panels = new List<Panel.SourcePanel>();

            var panels = GetComponentsInChildren<Panel.SourcePanel>(true);
            foreach (var panel in panels)
                _panels.Add(panel);

            _panels.ForEach(panel => panel.Init(this));

            _canvas.enabled = IsOpenOnStart;

            foreach (var panel in _panels)
            {
                if (panel.isOpenOnInit)
                {
                    panel.OnOpen();
                    if (!panel.isAlwaysOpen) _current = panel;   // стартовый экран = корень истории
                }
                else
                    panel.OnCLose();
            }

            IsInited = true;
            gameObject.SetActive(true);
        }

        public virtual void OnInject() { }

        public virtual void InvokeCanvas()
        {
            _canvas.enabled = true;
            UIEventBus.Publish(new CanvasOpenedEvent { CanvasType = GetType(), Canvas = this });
        }

        public virtual void CloseCanvas()
        {
            _canvas.enabled = false;
            _panels.ForEach(panel => panel.OnCLose());
            UIEventBus.Publish(new CanvasClosedEvent { CanvasType = GetType(), Canvas = this });
        }

        public virtual void Dispose()
        {
            _panels.ForEach(panel => panel.OnDipose());
            UIEventBus.UnsubscribeAll(this);
        }

        public virtual T OpenPanel<T>(params Action[] callback) where T : IPanel
        {
            IPanel found = null;
            foreach (var sourcePanel in _panels)
                if (sourcePanel is T) { found = sourcePanel; break; }

            return (T)OpenInternal(found, record: true, callback);
        }

        public virtual IPanel OpenPanel(IPanel panel, params Action[] callback)
            => OpenInternal(panel, record: true, callback);

        public virtual IPanel OpenPanel(string panelId, params Action[] callback)
            => OpenInternal(GetPanelInstance(panelId), record: true, callback);

        public virtual IPanel GetPanel(string panelId) => GetPanelInstance(panelId);

        Panel.SourcePanel GetPanelInstance(string panelId)
        {
            if (string.IsNullOrEmpty(panelId)) return null;
            foreach (var p in _panels)
                if (p.PanelId == panelId) return p;
            return null;
        }

        // Ядро открытия: держит isAlwaysOpen открытыми, закрывает остальные, ведёт журнал истории.
        IPanel OpenInternal(IPanel target, bool record, Action[] callback)
        {
            if (target == null) return null;

            foreach (var sourcePanel in _panels)
            {
                if (sourcePanel.isAlwaysOpen) { sourcePanel.OnOpen(callback); continue; }
                if (!ReferenceEquals(sourcePanel, target)) sourcePanel.OnCLose();
            }

            if (record && _current != null && !ReferenceEquals(_current, target))
                _history.Push(_current);

            if (!target.isOpen) target.OnOpen(callback);
            _current = target;
            return target;
        }

        /// <summary>Вернуться на предыдущую панель из журнала истории (жест/кнопка «назад»).</summary>
        public virtual bool Back()
        {
            if (_history.Count == 0) return false;
            var prev = _history.Pop();
            OpenInternal(prev, record: false, System.Array.Empty<Action>());
            return true;
        }

        public virtual T ClosePanel<T>() where T : IPanel
        {
            IPanel returnedPanel = null;

            foreach (var sourcePanel in _panels)
            {
                if (sourcePanel.isAlwaysOpen)
                {
                    sourcePanel.OnOpen();
                    continue;
                }

                if (sourcePanel is T panel)
                    returnedPanel = panel;
            }

            returnedPanel?.OnCLose();
            return (T)returnedPanel;
        }

        public virtual T GetPanel<T>() where T : class, IPanel
        {
            foreach (var sourcePanel in _panels)
            {
                if (sourcePanel is T panel)
                    return panel;
            }
            return null;
        }

        public virtual bool TryGetPanel<T>(out T returnedPanel) where T : class, IPanel
        {
            returnedPanel = GetPanel<T>();
            return returnedPanel != null;
        }
    }
}