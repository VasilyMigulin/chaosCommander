using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Core.Events
{
    /// <summary>
    /// Глобальная шина игровых событий
    /// </summary>
    public static class GameEventBus
    {
        private static readonly Dictionary<Type, List<Delegate>> _subscribers = new();
        private static readonly Dictionary<object, List<(Type eventType, Delegate handler)>> _ownerSubscriptions = new();

        /// <summary>
        /// Подписаться на игровое событие
        /// </summary>
        public static void Subscribe<T>(Action<T> handler) where T : struct, IGameEvent
        {
            var type = typeof(T);
            if (!_subscribers.ContainsKey(type))
                _subscribers[type] = new List<Delegate>();

            _subscribers[type].Add(handler);
        }

        /// <summary>
        /// Подписаться с привязкой к владельцу (для автоматической отписки)
        /// </summary>
        public static void Subscribe<T>(object owner, Action<T> handler) where T : struct, IGameEvent
        {
            Subscribe(handler);

            if (!_ownerSubscriptions.ContainsKey(owner))
                _ownerSubscriptions[owner] = new List<(Type, Delegate)>();

            _ownerSubscriptions[owner].Add((typeof(T), handler));
        }

        /// <summary>
        /// Отписаться от события
        /// </summary>
        public static void Unsubscribe<T>(Action<T> handler) where T : struct, IGameEvent
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
        public static void Publish<T>(T evt) where T : struct, IGameEvent
        {
            var type = typeof(T);
            if (!_subscribers.TryGetValue(type, out var handlers))
                return;

            var handlersCopy = new List<Delegate>(handlers);
            foreach (var handler in handlersCopy)
            {
                try
                {
                    ((Action<T>)handler)?.Invoke(evt);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[GameEventBus] Error invoking handler for {type.Name}: {e.Message}");
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
}