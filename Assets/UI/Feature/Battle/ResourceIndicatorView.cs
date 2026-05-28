using Game.Core.Events;
using Game.Core.Service;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Feature.Battle
{
    /// <summary>
    /// Отображает значение одного ресурса (Gold или Mana) для конкретного игрока.
    /// Подписывается на ResourceChangedEvent и обновляет текст/слайдер.
    /// </summary>
    public class ResourceIndicatorView : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private EnumService.ResourceType _resourceType;
        [SerializeField] private bool _isLocalPlayer = true;

        [Header("References")]
        [SerializeField] private TextMeshProUGUI _valueText;
        [SerializeField] private Slider          _fillSlider;
        [SerializeField] private Image           _icon;

        private int _localPlayerId = -1;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        public void OnInject(int localPlayerId)
        {
            _localPlayerId = localPlayerId;
            GameEventBus.Subscribe<ResourceChangedEvent>(OnResourceChanged);
        }

        public void Unject()
        {
            GameEventBus.Unsubscribe<ResourceChangedEvent>(OnResourceChanged);
        }

        // ── Event handler ─────────────────────────────────────────────────────

        private void OnResourceChanged(ResourceChangedEvent evt)
        { 
            if (evt.isLocalPlayer && evt.Type == _resourceType)
            {  
                UpdateDisplay(evt.NewValue, evt.MaxValue);
            } 
        }

        // ── Display ───────────────────────────────────────────────────────────

        private void UpdateDisplay(int current, int max)
        {
            if (_valueText != null) _valueText.text = $"{current}/{max}"; 
            if (_fillSlider  != null && max > 0)
                _fillSlider.value = (float)current / max;
        }
    }
}
