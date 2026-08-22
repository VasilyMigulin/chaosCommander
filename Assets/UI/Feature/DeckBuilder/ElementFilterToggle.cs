using System;
using Game.Core.Service;
using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Feature.DeckBuilder
{
    /// <summary>
    /// Кнопка-тогл цветового фильтра библиотеки в DeckBuildPanel (один на EnumService.Element,
    /// см. CollectionFilterBarView). Клик включает/выключает фильтр по этому цвету; визуально —
    /// один готовый art-объект (кристалл), просто SetActive(IsOn) — фон уже даёт сам Button
    /// (стандартный ColorTint-transition), второй слой не нужен.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class ElementFilterToggle : MonoBehaviour
    {
        [SerializeField] EnumService.Element _element;

        [Tooltip("Показывается, когда фильтр включён (IsOn), прячется, когда выключен.")]
        [SerializeField] GameObject _crystal;

        Button _button;

        public EnumService.Element Element => _element;
        public bool IsOn { get; private set; }

        /// <summary>Цвет + новое состояние.</summary>
        public event Action<EnumService.Element, bool> OnToggled;

        void Awake()
        {
            _button = GetComponent<Button>();
            _button.onClick.AddListener(Toggle);
            Apply();
        }

        void OnDestroy()
        {
            if (_button != null) _button.onClick.RemoveListener(Toggle);
        }

        void Toggle() => SetOn(!IsOn);

        /// <summary>Проставить состояние. notify=false — тихо, без события (сброс фильтров при открытии).</summary>
        public void SetOn(bool value, bool notify = true)
        {
            IsOn = value;
            Apply();
            if (notify) OnToggled?.Invoke(_element, IsOn);
        }

        void Apply()
        {
            if (_crystal != null) _crystal.SetActive(IsOn);
        }

        public void SetInteractable(bool interactable)
        {
            if (_button != null) _button.interactable = interactable;
        }
    }
}
