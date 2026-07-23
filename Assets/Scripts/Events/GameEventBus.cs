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

        // Отдельное хранилище для persistent UI (DontDestroyOnLoad-канвасы вроде BattleCanvas): их Awake/
        // OnEnable/Init отрабатывают ОДИН раз за всю сессию приложения, а не на каждый матч, поэтому обычная
        // Subscribe() тут теряется НАВСЕГДА после первого же Clear() (см. EcsRunHandler.Dispose при выходе
        // из боя — чистит ECS-подписки систем каждого матча). Clear() эту таблицу не трогает.
        private static readonly Dictionary<Type, List<Delegate>> _persistentSubscribers = new();

        /// <summary>Подписка, переживающая GameEventBus.Clear() — для persistent UI-компонентов, чей
        /// Awake()/OnEnable()/Init() вызывается один раз за сессию (см. _persistentSubscribers).</summary>
        public static void SubscribePersistent<T>(Action<T> handler) where T : struct, IGameEvent
        {
            var type = typeof(T);
            if (!_persistentSubscribers.TryGetValue(type, out var list))
                _persistentSubscribers[type] = list = new List<Delegate>();
            if (!list.Contains(handler)) list.Add(handler);
        }

        public static void UnsubscribePersistent<T>(Action<T> handler) where T : struct, IGameEvent
        {
            var type = typeof(T);
            if (_persistentSubscribers.TryGetValue(type, out var list))
                list.Remove(handler);
        }

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

            if (_subscribers.TryGetValue(type, out var handlers))
                InvokeAll(type, evt, handlers);

            if (_persistentSubscribers.TryGetValue(type, out var persistentHandlers))
                InvokeAll(type, evt, persistentHandlers);
        }

        private static void InvokeAll<T>(Type type, T evt, List<Delegate> handlers) where T : struct, IGameEvent
        {
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