using System;
using AwesomeUI.Core.Slot;
using Game.Core.Service;
using Game.Core.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace AwesomeUI.Core.Card
{
    /// <summary>
    /// Базовый компонент карты. Содержит все общие UI-поля:
    ///   - Бэкграунд редкости
    ///   - Название, тип, описание
    ///   - Стоимость (значение + иконки Gold/Mana/Health)
    ///   - Индикаторы элемента (массив, включается нужный)
    ///   - Стат-блок существа (Attack / Health / Speed) — скрывается для заклинаний и чармов
    ///
    /// Наследники вызывают ApplyVisualData(CardVisualData) чтобы заполнить все поля.
    /// </summary>
    public abstract class CardBaseView : SourceSlot, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        // ── Редкость ──────────────────────────────────────────────────────────

        [Header("Rarity")]
        [SerializeField] Image _rarityBadge;
        [SerializeField] RarityColorEntry[] _rarityColors;

        [Serializable]
        public struct RarityColorEntry
        {
            public EnumService.Rarity Rarity;
            public Sprite             Sprite;
        }

        // ── Бэкграунд карты ───────────────────────────────────────────────────

        [Header("Background")]
        [SerializeField] Image                    _cardBackground;
        [SerializeField] RarityBackgroundEntry[] _cardBackgrounds;
        [SerializeField] CardHighlightEffect     _highlight;

        [Serializable]
        public struct RarityBackgroundEntry
        {
            public EnumService.Rarity Rarity;
            public Sprite               Sprite;
        }

        // ── Арт карты ────────────────────────────────────────────────────────

        [Header("Art")]
        [SerializeField] Image _artImage;

        // ── Основная информация ───────────────────────────────────────────────

        [Header("Card Info")]
        [SerializeField] TextMeshProUGUI _nameText;
        [SerializeField] TextMeshProUGUI _typeText;
        [SerializeField] TextMeshProUGUI _descriptionText;

        // ── Стоимость ─────────────────────────────────────────────────────────

        [Header("Cost")]
        [SerializeField] TextMeshProUGUI   _costText;
        [SerializeField] Image             _costIcon;
        [SerializeField] CostIconEntry[]   _costIcons;

        [Serializable]
        public struct CostIconEntry
        {
            public EnumService.ResourceType ResourceType;
            public Sprite                   Sprite;
        }

        // ── Элемент ───────────────────────────────────────────────────────────

        [Header("Element Indicators")]
        [SerializeField] ElementIndicatorEntry[] _elementIndicators;

        [Serializable]
        public struct ElementIndicatorEntry
        {
            public EnumService.Element Element;
            public GameObject          Indicator;
        }

        // ── Стат-блок существа ────────────────────────────────────────────────

        [Header("Creature Stats")]
        [SerializeField] GameObject      _creatureStatsRoot;
        [SerializeField] TextMeshProUGUI _attackText;
        [SerializeField] TextMeshProUGUI _healthText;
        [SerializeField] TextMeshProUGUI _speedText;

        // ── API ───────────────────────────────────────────────────────────────

        /// <summary>Публичный рендер полной карты из CardVisualData (для дисплейных вьюшек вне слот-контекста,
        /// напр. выкат «оппонент разыграл карту»). Внутри — тот же ApplyVisualData.</summary>
        public void SetVisual(in CardVisualData data) => ApplyVisualData(data);

        protected void ApplyVisualData(in CardVisualData data)
        {
            ApplyRarity(data.Rarity);
            ApplyBackground(data.Rarity);
            ApplyArt(data.Icon);
            ApplyInfo(data.CardName, data.CardType, data.Description);
            ApplyCost(data.CostType, data.CostAmount);
            ApplyElement(data.Element);
            ApplyCreatureStats(data.IsCreature, data.Attack, data.MaxHealth, data.Speed);
        }

        public void SetHighlight(CardHighlightEffect.HighlightType type, bool active)
        {
            if (_highlight != null)
                _highlight.SetState(type, active);
        }

        public void ResetHighlight()
        {
            if (_highlight != null)
                _highlight.ResetAll();
        }

        /// <summary>
        /// Перестроить локализованный текст карты при смене языка (CardLocalizationRefresher зовёт это
        /// на LocaleService.Changed). База — no-op; модель-driven вьюшки переопределяют и пересобирают
        /// CardVisualData из своей Model.
        /// </summary>
        public virtual void RefreshLocalization() { }

        // ── Art ──────────────────────────────────────────────────────────────

        void ApplyArt(Sprite icon)
        {
            if (_artImage == null) return;
            _artImage.sprite  = icon;
            _artImage.enabled = icon != null;
        }

        // ── Rarity ───────────────────────────────────────────────────────────

        void ApplyRarity(EnumService.Rarity rarity)
        {
            if (_rarityBadge == null || _rarityColors == null) return;
            foreach (var entry in _rarityColors)
            {
                if (entry.Rarity == rarity)
                {
                    _rarityBadge.sprite = entry.Sprite;
                    return;
                }
            }
        }

        // ── Background ───────────────────────────────────────────────────────

        void ApplyBackground(EnumService.Rarity rarity)
        {
            if (_cardBackground == null || _cardBackgrounds == null) return;
            foreach (var entry in _cardBackgrounds)
            {
                if (entry.Rarity == rarity)
                {
                    _cardBackground.sprite = entry.Sprite;
                    return;
                }
            }
        }

        // ── Info ─────────────────────────────────────────────────────────────

        void ApplyInfo(string cardName, EnumService.CardType cardType, string description)
        {
            if (_nameText        != null) _nameText.text        = cardName    ?? "";
            if (_typeText        != null) _typeText.text        = CardTypeLabel(cardType);
            if (_descriptionText != null) _descriptionText.text = description ?? "";
        }

        // ── Cost ─────────────────────────────────────────────────────────────

        /// <summary>Обновить ТОЛЬКО число стоимости (эффективная цена с модификатором — Гиперинфляция).
        /// Тип/иконку не трогаем. Зовёт PlayCardView по CardCostChangedEvent.</summary>
        public void SetCostAmount(int amount)
        {
            if (_costText != null) _costText.text = amount.ToString();
        }

        void ApplyCost(EnumService.ResourceType costType, int amount)
        {
            if (_costText != null) _costText.text = amount.ToString();

            if (_costIcon == null || _costIcons == null) return;
            foreach (var entry in _costIcons)
            {
                if (entry.ResourceType == costType)
                {
                    _costIcon.sprite = entry.Sprite;
                    return;
                }
            }
        }

        // ── Element ─────────────────────────────────────────────────────────── 
        void ApplyElement(EnumService.Element element)
        {
            if (_elementIndicators == null) return;
            foreach (var entry in _elementIndicators)
            {
                if (entry.Indicator != null)
                    entry.Indicator.SetActive((element & entry.Element) != 0);
            }
        }

        // ── Creature Stats ──────────────────────────────────────────────────── 
        void ApplyCreatureStats(bool isCreature, int attack, int health, int speed)
        {
            if (_creatureStatsRoot != null)
                _creatureStatsRoot.SetActive(isCreature);

            if (!isCreature) return;

            if (_attackText != null) _attackText.text = attack.ToString();
            if (_healthText != null) _healthText.text = health.ToString();
            if (_speedText  != null) _speedText.text  = speed.ToString();
        }

        // ── Helpers ─────────────────────────────────────────────────────────── 
        static string CardTypeLabel(EnumService.CardType type)
            => Game.Core.Shared.CardTextLocalization.TypeLabel(type);

        // ── Hold-inspect (удержание карты → предпросмотр) ─────────────────────
        // Детект удержания живёт в базе, чтобы был у всех карт (библиотека/колода/попап).
        // Что делать по удержанию — решает наследник в OnHoldTriggered (open предпросмотр своей Model).

        [Header("Hold Inspect")]
        [SerializeField] bool  _holdInspectEnabled = true;
        [SerializeField] float _holdThreshold = 0.45f;

        bool  _pressed;
        bool  _holdFired;
        bool  _holdClickConsumed;
        float _pressTime;

        protected virtual void Update()
        {
            if (!_pressed || _holdFired || !_holdInspectEnabled) return;
            if (Time.unscaledTime - _pressTime < _holdThreshold) return;

            _holdFired = true;
            _holdClickConsumed = true;   // погасить последующий onClick (add/remove)
            OnHoldTriggered();
        }

        /// <summary>Вызывается при удержании ≥ порога. Наследник открывает предпросмотр своей карты.</summary>
        protected virtual void OnHoldTriggered() { }

        /// <summary>Наследник зовёт первым в OnClick: true → это был hold-предпросмотр, обычное действие пропустить.</summary>
        protected bool ConsumeHoldClick()
        {
            if (!_holdClickConsumed) return false;
            _holdClickConsumed = false;
            return true;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _pressed = true;
            _holdFired = false;
            _holdClickConsumed = false;
            _pressTime = Time.unscaledTime;
        }

        public void OnPointerUp(PointerEventData eventData)   => _pressed = false;
        public void OnPointerExit(PointerEventData eventData) => _pressed = false;
    }
}
