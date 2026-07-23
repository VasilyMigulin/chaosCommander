using Game.Core.Events;
using Game.Core.Service;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Feature.Battle
{
    /// <summary>
    /// Отображает значение одного ресурса (Gold/Mana/Health) ЛИБО счётчик руки/колоды — для локального
    /// игрока, в одной и той же ресурс-панели (доп. слот того же компонента, Kind=HandCount/DeckCount, вместо
    /// отдельного класса — переиспользуем и вёрстку, и подписку/анти-дублирующее обновление текста).
    /// </summary>
    public class ResourceIndicatorView : MonoBehaviour
    {
        public enum Kind { Resource, HandCount, DeckCount }

        [Header("Settings")]
        [SerializeField] private Kind _kind = Kind.Resource;
        [Tooltip("Только для Kind=Resource.")]
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
            if (_kind == Kind.Resource) GameEventBus.Subscribe<ResourceChangedEvent>(OnResourceChanged);
            else                        GameEventBus.Subscribe<HandDeckCountChangedUIEvent>(OnHandDeckChanged);
        }

        public void Unject()
        {
            GameEventBus.Unsubscribe<ResourceChangedEvent>(OnResourceChanged);
            GameEventBus.Unsubscribe<HandDeckCountChangedUIEvent>(OnHandDeckChanged);
        }

        // ── Event handlers ────────────────────────────────────────────────────

        private void OnResourceChanged(ResourceChangedEvent evt)
        {
            if (evt.isLocalPlayer && evt.Type == _resourceType)
            {
                UpdateDisplay(evt.NewValue, evt.MaxValue);
            }
        }

        // Событие публикуется ТОЛЬКО для локального (см. PlayerStatsViewSystem) — фильтровать по игроку не нужно.
        private void OnHandDeckChanged(HandDeckCountChangedUIEvent evt)
        {
            if (_kind == Kind.HandCount) UpdateDisplay(evt.HandCount, evt.HandMax);
            else if (_kind == Kind.DeckCount) UpdateDisplay(evt.DeckCount, 0);   // у колоды нет фикс. максимума
        }

        // ── Display ───────────────────────────────────────────────────────────

        private void UpdateDisplay(int current, int max)
        {
            if (_valueText != null) _valueText.text = max > 0 ? $"{current}/{max}" : current.ToString();
            if (_fillSlider  != null && max > 0)
                _fillSlider.value = (float)current / max;
        }
    }
}
