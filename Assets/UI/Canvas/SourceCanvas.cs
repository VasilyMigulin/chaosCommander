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
                    panel.OnOpen();
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
            IPanel returnedPanel = null;

            foreach (var sourcePanel in _panels)
            {
                if (sourcePanel.isAlwaysOpen)
                {
                    sourcePanel.OnOpen(callback);
                    continue;
                }

                if (sourcePanel is T panel)
                    returnedPanel = panel;
                else
                    sourcePanel.OnCLose();
            }

            if (returnedPanel != null && !returnedPanel.isOpen)
                returnedPanel.OnOpen(callback);

            return (T)returnedPanel;
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

        public virtual T GetPanel<T>() where T : Panel.SourcePanel
        {
            foreach (var sourcePanel in _panels)
            {
                if (sourcePanel is T panel)
                    return panel;
            }
            return null;
        }

        public virtual bool TryGetPanel<T>(out T returnedPanel) where T : Panel.SourcePanel
        {
            returnedPanel = GetPanel<T>();
            return returnedPanel != null;
        }
    }
}