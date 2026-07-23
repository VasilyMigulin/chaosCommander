using AwesomeUI.Core.Canvas;
using AwesomeUI.Core.Events;
using AwesomeUI.Core.Handler;
using AwesomeUI.Feature;
using System;
using UnityEngine;

namespace AwesomeUI.Core
{
    /// <summary>
    /// Главная точка входа в AwesomeUI
    /// </summary>
    public static class UIModule
    {
        private static UIHandler _handler;
        private static bool _isInitialized;

        public static bool IsInitialized => _isInitialized;
        public static UIHandler Handler => _handler;

        /// <summary>
        /// Инициализация UI системы
        /// </summary>
        /// <param name="handlerPrefabPath">Путь к префабу в Resources (опционально)</param>
        public static void Initialize(string handlerPrefabPath = null)
        {
            if (_isInitialized)
            {
                Debug.LogWarning("[AwesomeUI] Already initialized!");
                return;
            }

            GameObject prefab = FindHandlerPrefab(handlerPrefabPath);

            if (prefab == null)
            {
                Debug.LogError("[AwesomeUI] UIHandler prefab not found in Resources!");
                return;
            }

            var instance = UnityEngine.Object.Instantiate(prefab);
            instance.name = "UIHandler";
            UnityEngine.Object.DontDestroyOnLoad(instance);

            _handler = instance.GetComponent<UIHandler>();

            if (_handler == null)
            {
                Debug.LogError("[AwesomeUI] Prefab doesn't have UIHandler component!");
                UnityEngine.Object.Destroy(instance);
                return;
            }

            _handler.Init();
            _isInitialized = true;

            // Системная «назад» на Android по умолчанию ЗАКРЫВАЕТ активити (Unity логирует «Quit requested»
            // → APP_CMD_DESTROY → процесс выходит с кодом 0). Со стороны это выглядит как краш: игра просто
            // сворачивается с любого экрана. Отдаём кнопку нам — Unity будет только слать KeyCode.Escape,
            // а навигацию делает BackGestureHandler → UIModule.Back(). На не-Android свойство игнорируется.
            Input.backButtonLeavesApp = false;

            // Глобальный обработчик жеста «назад» (Android hardware/gesture + iOS свайп-от-края).
            if (instance.GetComponent<BackGestureHandler>() == null)
                instance.AddComponent<BackGestureHandler>();

            Debug.Log("[AwesomeUI] Initialized successfully");
        }
         
        private static GameObject FindHandlerPrefab(string path)
        {
            if (!string.IsNullOrEmpty(path))
            {
                var prefab = Resources.Load<GameObject>(path);
                if (prefab != null && prefab.GetComponent<UIHandler>() != null)
                    return prefab;
            }

            // Поиск по всем Resources
            foreach (var prefab in Resources.LoadAll<GameObject>(""))
            {
                if (prefab.GetComponent<UIHandler>() != null)
                    return prefab;
            }

            return null;
        }

        /// <summary>
        /// Инъекция зависимостей в UI элементы
        /// </summary>
        public static void Inject(MonoBehaviour owner, params object[] data)
        {
            EnsureInitialized();

            _handler.Inject(data);

            // Автоматическая отписка при уничтожении owner
            if (!owner.TryGetComponent<InjectionDestroyHook>(out var hook))
                hook = owner.gameObject.AddComponent<InjectionDestroyHook>();

            hook.OnDestroyed -= OnOwnerDestroyed;
            hook.OnDestroyed += OnOwnerDestroyed;
        }

        private static void OnOwnerDestroyed()
        {
            _handler?.Unject();
        }

        #region Canvas API

        /// <summary>
        /// Открыть Canvas по типу
        /// </summary>
        public static T Open<T>() where T : SourceCanvas
        {
            EnsureInitialized();
            _handler.InvokeCanvas<T>(out var canvas);
            return canvas;
        }

        /// <summary>
        /// Открыть Canvas по типу с callback
        /// </summary>
        public static T Open<T>(Action onOpened) where T : SourceCanvas
        {
            EnsureInitialized();

            void Handler(CanvasOpenedEvent evt)
            {
                if (evt.CanvasType == typeof(T))
                {
                    onOpened?.Invoke();
                    UIEventBus.Unsubscribe<CanvasOpenedEvent>(Handler);
                }
            }

            UIEventBus.Subscribe<CanvasOpenedEvent>(Handler);
            return Open<T>();
        }

        /// <summary>
        /// Закрыть Canvas по типу
        /// </summary>
        public static T Close<T>() where T : SourceCanvas
        {
            EnsureInitialized();
            _handler.CloseCanvas<T>(out var canvas);
            return canvas;
        }

        /// <summary>
        /// Жест/кнопка «назад»: вернуться на предыдущую панель активного канваса.
        /// false — если возвращаться некуда (корневой экран) — вызывающий может выйти из приложения.
        /// </summary>
        public static bool Back()
        {
            if (!_isInitialized) return false;
            return _handler.BackActiveCanvas();
        }

        /// <summary>
        /// Получить Canvas по типу (без открытия)
        /// </summary>
        public static T Get<T>() where T : SourceCanvas
        {
            EnsureInitialized();
            _handler.TryGetCanvas<T>(out var canvas);
            return canvas;
        }

        /// <summary>
        /// Попытаться получить Canvas по типу
        /// </summary>
        public static bool TryGet<T>(out T canvas) where T : SourceCanvas
        {
            EnsureInitialized();
            return _handler.TryGetCanvas(out canvas);
        }

        #endregion

        #region Event Bus Shortcuts

        /// <summary>
        /// Подписаться на UI событие
        /// </summary>
        public static void On<T>(Action<T> handler) where T : IUIEvent
            => UIEventBus.Subscribe(handler);

        /// <summary>
        /// Подписаться на UI событие с привязкой к владельцу
        /// </summary>
        public static void On<T>(object owner, Action<T> handler) where T : IUIEvent
            => UIEventBus.Subscribe(owner, handler);

        /// <summary>
        /// Отписаться от UI события
        /// </summary>
        public static void Off<T>(Action<T> handler) where T : IUIEvent
            => UIEventBus.Unsubscribe(handler);

        /// <summary>
        /// Опубликовать UI событие
        /// </summary>
        public static void Emit<T>(T evt) where T : IUIEvent
            => UIEventBus.Publish(evt);

        #endregion

        /// <summary>
        /// Очистить все UI ресурсы
        /// </summary>
        public static void Dispose()
        {
            if (!_isInitialized)
                return;

            _handler?.Dispose();
            UIEventBus.Clear();
            _isInitialized = false;
        }

        private static void EnsureInitialized()
        {
            if (!_isInitialized)
                throw new InvalidOperationException("[AwesomeUI] Not initialized! Call UIModule.Initialize() first.");
        }
    }

    /// <summary>
    /// Хук для отслеживания уничтожения объекта
    /// </summary>
    public class InjectionDestroyHook : MonoBehaviour
    {
        public event Action OnDestroyed;

        private void OnDestroy()
        {
            OnDestroyed?.Invoke();
        }
    }
}