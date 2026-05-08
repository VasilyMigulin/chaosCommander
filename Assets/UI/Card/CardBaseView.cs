using System;
using AwesomeUI.Core.Slot;
using Game.Core.Service;
using Game.Core.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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
    public abstract class CardBaseView : SourceSlot
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
        {
            switch (type)
            {
                case EnumService.CardType.Creature: return "Существо";
                case EnumService.CardType.Spell:    return "Заклинание";
                case EnumService.CardType.Charm:    return "Чары";
                default:                            return "";
            }
        }
    }
}
