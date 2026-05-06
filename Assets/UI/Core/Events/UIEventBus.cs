using System;
using System.Collections.Generic;

namespace AwesomeUI.Core.Events
{
    /// <summary>
    /// Глобальная шина событий для UI
    /// </summary>
    public static class UIEventBus
    {
        private static readonly Dictionary<Type, List<Delegate>> _subscribers = new Dictionary<Type, List<Delegate>>();
        private static readonly Dictionary<object, List<(Type eventType, Delegate handler)>> _ownerSubscriptions 
            = new Dictionary<object, List<(Type, Delegate)>>();

        /// <summary>
        /// Подписаться на событие
        /// </summary>
        public static void Subscribe<T>(Action<T> handler) where T : IUIEvent
        {
            var type = typeof(T);
            if (!_subscribers.ContainsKey(type))
                _subscribers[type] = new List<Delegate>();

            _subscribers[type].Add(handler);
        }

        /// <summary>
        /// Подписаться на событие с привязкой к владельцу (для автоматической отписки)
        /// </summary>
        public static void Subscribe<T>(object owner, Action<T> handler) where T : IUIEvent
        {
            Subscribe(handler);

            if (!_ownerSubscriptions.ContainsKey(owner))
                _ownerSubscriptions[owner] = new List<(Type, Delegate)>();

            _ownerSubscriptions[owner].Add((typeof(T), handler));
        }

        /// <summary>
        /// Отписаться от события
        /// </summary>
        public static void Unsubscribe<T>(Action<T> handler) where T : IUIEvent
        {
            var type = typeof(T);
            if (_subscribers.ContainsKey(type))
                _subscribers[type].Remove(handler);
        }

        /// <summary>
        /// Отписать все события владельца
        /// </summary>
        public static void UnsubscribeAll(object owner)
        {
            if (!_ownerSubscriptions.TryGetValue(owner, out var subscriptions))
                return;

            foreach (var (eventType, handler) in subscriptions)
            {
                if (_subscribers.TryGetValue(eventType, out var handlers))
                    handlers.Remove(handler);
            }

            _ownerSubscriptions.Remove(owner);
        }

        /// <summary>
        /// Опубликовать событие
        /// </summary>
        public static void Publish<T>(T evt) where T : IUIEvent
        {
            var type = typeof(T);
            if (!_subscribers.TryGetValue(type, out var handlers))
                return;

            // Копируем список чтобы избежать проблем при модификации во время итерации
            var handlersCopy = new List<Delegate>(handlers);
            foreach (var handler in handlersCopy)
            {
                try
                {
                    ((Action<T>)handler)?.Invoke(evt);
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogError($"[UIEventBus] Error invoking handler for {type.Name}: {e.Message}");
                }
            }
        }

        /// <summary>
        /// Очистить все подписки
        /// </summary>
        public static void Clear()
        {
            _subscribers.Clear();
            _ownerSubscriptions.Clear();
        }
    }

    /// <summary>
    /// Маркерный интерфейс для UI событий
    /// </summary>
    public interface IUIEvent { }

    #region Встроенные события

    /// <summary>
    /// Событие открытия Canvas
    /// </summary>
    public struct CanvasOpenedEvent : IUIEvent
    {
        public Type CanvasType;
        public object Canvas;
    }

    /// <summary>
    /// Событие закрытия Canvas
    /// </summary>
    public struct CanvasClosedEvent : IUIEvent
    {
        public Type CanvasType;
        public object Canvas;
    }

    /// <summary>
    /// Событие открытия Panel
    /// </summary>
    public struct PanelOpenedEvent : IUIEvent
    {
        public Type PanelType;
        public object Panel;
    }

    /// <summary>
    /// Событие закрытия Panel
    /// </summary>
    public struct PanelClosedEvent : IUIEvent
    {
        public Type PanelType;
        public object Panel;
    }

    /// <summary>
    /// Событие нажатия кнопки в UI
    /// </summary>
    public struct UIButtonClickEvent : IUIEvent
    {
        public string ButtonId;
        public object Sender;
    }

    #endregion
}