using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace AwesomeUI.Service
{
    [RequireComponent(typeof(UnityEngine.UI.Button), typeof(InvisibleGraphic))]
    public class ButtonHoldListener : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [Tooltip("Срабатывает при нажатии (PointerDown)")]
        public UnityEvent OnPress;

        [Tooltip("Срабатывает при отпускании (PointerUp или выход курсора)")]
        public UnityEvent OnRelease;

        [System.Serializable]
        public class HoldEvent : UnityEvent<float> { }

        [Tooltip("Вызывается каждую рамку удержания, передаёт время удержания в секундах")]
        public HoldEvent OnHold;

        [Header("Настройки удержания")]
        [Tooltip("Порог времени удержания, после которого сработает OnHoldComplete (в секундах)")]
        public float holdThreshold = 2f;

        [Tooltip("Срабатывает один раз, когда удержание превысит holdThreshold")]
        public UnityEvent OnHoldComplete;

        private bool isPressed;
        private bool thresholdTriggered;
        private float holdTime;

        public void Init()
        {

        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!isPressed)
            {
                isPressed = true;
                thresholdTriggered = false;
                holdTime = 0f;
                OnPress?.Invoke();
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            Release();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            // Если курсор ушёл с кнопки, считаем это отпусканием
            if (isPressed && eventData.pointerPress == gameObject)
            {
                Release();
            }
        }

        private void Release()
        {
            if (isPressed)
            {
                isPressed = false;
                OnRelease?.Invoke();
            }
        }

        private void Update()
        {
            if (isPressed)
            {
                holdTime += Time.unscaledDeltaTime; // без влияния Time.timeScale
                OnHold?.Invoke(holdTime);

                if (!thresholdTriggered && holdTime >= holdThreshold)
                {
                    thresholdTriggered = true;
                    OnHoldComplete?.Invoke();
                }
            }
        }

        public bool IsPressed => isPressed;
        public float HoldTime => holdTime;

        public void RemoveListeners()
        {
            OnPress?.RemoveAllListeners();
            OnRelease?.RemoveAllListeners();
            OnHold?.RemoveAllListeners();
            OnHoldComplete?.RemoveAllListeners();
        }
    }
}