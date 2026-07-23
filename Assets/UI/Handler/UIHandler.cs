using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

using SourceCanvas = AwesomeUI.Core.Canvas.SourceCanvas;
using SourcePanel = AwesomeUI.Core.Panel.SourcePanel;
using SourceLayout = AwesomeUI.Core.Layout.SourceLayout;
using SourceWindow = AwesomeUI.Core.Window.SourceWindow;
using SourceSlot = AwesomeUI.Core.Slot.SourceSlot;
using AwesomeUI.Service; 

namespace AwesomeUI.Core.Handler
{
    public class UIHandler : MonoBehaviour
    {
        private List<SourceCanvas> _canvases = new();
        private List<SourcePanel> _panels = new();
        private List<SourceLayout> _layouts = new();
        private List<SourceWindow> _windows = new();
        private List<SourceSlot> _slots = new();

        [Tooltip("Глобальные конфиги (ScriptableObject), доступные ЛЮБОМУ UI-элементу через GetConfig<T>() — "
                + "в т.ч. динамически создаваемым инстансам (напр. карточки в библиотеке/руке), которые не "
                + "попадают в обычный [UIInject]-скан (тот берёт слепок иерархии ОДИН раз в Init()). "
                + "UIHandler — persistent DontDestroyOnLoad-синглтон, поэтому это надёжнее Resources.Load "
                + "(нет зависимости от точного пути в Resources/гонки с индексацией ассета).")]
        [SerializeField] private UnityEngine.ScriptableObject[] _globalConfigs;

        /// <summary>Найти глобальный конфиг нужного типа среди перетащенных в инспекторе UIHandler.
        /// Null, если такого типа нет в списке — вызывающий сам решает, что делать (напр. no-op).</summary>
        public T GetConfig<T>() where T : UnityEngine.Object
        {
            if (_globalConfigs == null) return null;
            foreach (var c in _globalConfigs)
                if (c is T match) return match;
            return null;
        }

        public void Init()
        {
            // Пушим конфиги в статические свойства-получатели ОДИН раз при старте — сами получатели (напр.
            // AwesomeUI.Core.Card.InterfaceConfig.Current) НЕ тянут отсюда данные сами: AwesomeUI.Core уже
            // ссылается на AwesomeUI.Feature, а тот — на AwesomeUI.Core.Card, так что обратная ссылка
            // Card→Handler/Core дала бы цикл сборок. Однонаправленно (Handler→Card) — безопасно.
            AwesomeUI.Core.Card.InterfaceConfig.Current = GetConfig<AwesomeUI.Core.Card.InterfaceConfig>();

            _canvases = GetComponentsInChildren<SourceCanvas>(true).ToList();
            _panels = GetComponentsInChildren<SourcePanel>(true).ToList();
            _layouts = GetComponentsInChildren<SourceLayout>(true).ToList();
            _windows = GetComponentsInChildren<SourceWindow>(true).ToList();
            _slots = GetComponentsInChildren<SourceSlot>(true).ToList();
            _canvases = new List<SourceCanvas>();

            var canveses = GetComponentsInChildren<SourceCanvas>(true);

            for (int i = 0; i < canveses.Length; i++)
            {
                _canvases.Add(canveses[i]);
            }

            _canvases.ForEach(canvas => canvas.Init());
        }

        public void Unject()
        {
            _canvases.UninjectAll<SourceCanvas>();
            _panels.UninjectAll<SourcePanel>();
            _layouts.UninjectAll<SourceLayout>();
            _windows.UninjectAll<SourceWindow>();
            _slots.UninjectAll<SourceSlot>();
        }

        public void Inject(params object[] data)
        {
            InjectList(_canvases, data);
            InjectList(_panels, data);
            InjectList(_layouts, data);
            InjectList(_windows, data);
            InjectList(_slots, data);

            foreach (var canvas in _canvases)
                canvas.OnInject();
            foreach (var panel in _panels)
                panel.OnInject();
            foreach (var layout in _layouts)
                layout.OnInject();
            foreach (var window in _windows)
                window.OnInject();
            foreach (var slot in _slots)
                slot.OnInject();
        }

        public void Dispose()
        {
            _canvases.ForEach(_canvas => _canvas.Dispose());
        }
        public virtual bool InvokeCanvas<T>(out T canvas) where T : SourceCanvas
        {
            canvas = null;

            foreach (var c in _canvases)
            {
                if (c is T matched)
                {
                    canvas = matched;
                    break;
                }
            }

            if (canvas != null)
            {
                foreach (var c in _canvases)
                {
                    if (c != canvas)
                        c.CloseCanvas();
                }

                canvas.InvokeCanvas();
                return true;
            }

            return false;
        }


        public virtual bool CloseCanvas<T>(out T canvas) where T : SourceCanvas
        {
            canvas = null;

            foreach (var c in _canvases)
            {
                if (c is T closed)
                {
                    canvas = closed;
                }
            }

            canvas.CloseCanvas();

            return canvas != null;
        }

        /// <summary>Отдать «назад» активному (видимому) канвасу. false — если некуда/нет активного.</summary>
        public bool BackActiveCanvas()
        {
            foreach (var c in _canvases)
                if (c.IsOpen) return c.Back();
            return false;
        }

        public virtual bool TryGetCanvas<T>(out T canvas) where T : SourceCanvas
        {
            canvas = null;

            foreach (var c in _canvases)
            {
                if (c is T returned)
                {
                    canvas = returned;
                    break;
                }
            }

            return canvas != null;
        }
        static void InjectList(IEnumerable<object> targets, params object[] sources)
        {
            foreach (var target in targets)
            {
                // Обходим всю иерархию (DeclaredOnly на каждом уровне). Иначе приватные поля
                // базовых классов не видны через тип наследника (как было с PlayCardView._state
                // у слота SimpleCardView) — модификатор доступа поля больше не важен.
                var fields = new List<FieldInfo>();
                for (var t = target.GetType(); t != null && t != typeof(object); t = t.BaseType)
                    fields.AddRange(t.GetFields(BindingFlags.Public | BindingFlags.NonPublic
                                                | BindingFlags.Instance | BindingFlags.DeclaredOnly));

                foreach (var field in fields)
                {
                    if (field.GetCustomAttribute<Attributes.UIInjectAttribute>() == null)
                        continue;

                    // Попробовать найти подходящий source
                    object match = null;
                    foreach (var source in sources)
                    {
                        if (source == null) continue;

                        if (field.FieldType.IsAssignableFrom(source.GetType()))
                        {
                            match = source;
                            break;
                        }
                    }

                    if (match == null) continue;

                    try
                    {
                        field.SetValue(target, match);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[UIInject] Ошибка инъекции в {target.GetType().Name}.{field.Name}: {e.Message}");
                    }
                }
            }
        }
    }
}