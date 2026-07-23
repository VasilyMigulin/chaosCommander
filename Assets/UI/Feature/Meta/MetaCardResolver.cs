using Game.Core.Backend;
using Game.Core.Configs;
using Game.Core.Model.Card;
using Game.Core.Service;
using UnityEngine;

namespace AwesomeUI.Feature
{
    /// <summary>Визуал карты для мета-панелей (рынок/аукцион/бустеры/награды): резолв по catalog ItemId.</summary>
    public struct MetaCardVisual
    {
        public bool Found;
        public string Name;
        public Sprite Art;
        public EnumService.Rarity Rarity;
        public EnumService.Element Element;
    }

    /// <summary>
    /// Разворачивает catalog ItemId ("{expansionId}_{cardId}") в визуал карты через CardConfig.
    /// Централизует CardItemId.TryParse + CardConfig.Get, чтобы каждый слот не тянул это сам.
    /// </summary>
    public static class MetaCardResolver
    {
        public static MetaCardVisual Resolve(CardConfig config, string itemId)
        {
            var visual = new MetaCardVisual { Found = false, Name = itemId };
            if (config == null || string.IsNullOrEmpty(itemId)) return visual;
            if (!CardItemId.TryParse(itemId, out var expansionId, out var cardId)) return visual;

            var instance = config.Get(expansionId, cardId);
            if (instance == null || instance.CardData == null) return visual;

            var m = instance.CardData;
            visual.Found = true;
            visual.Name = m.Name;
            visual.Art = m.ArtImage;
            visual.Rarity = m.Rarity;
            visual.Element = m.Element;
            return visual;
        }

        /// <summary>ItemId → CardModel (полная модель для StaticCardView, единый шаблон карты). null, если не найдена.</summary>
        public static CardModel ResolveModel(CardConfig config, string itemId)
        {
            if (config == null || string.IsNullOrEmpty(itemId)) return null;
            if (!CardItemId.TryParse(itemId, out var expansionId, out var cardId)) return null;
            return config.Get(expansionId, cardId)?.CardData;
        }
    }
}
