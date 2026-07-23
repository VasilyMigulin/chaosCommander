using Game.Core.Service;
using UnityEngine;

namespace AwesomeUI.Feature
{
    /// <summary>Единая палитра цветов редкости для мета-UI (рамки карт, бейджи офферов).</summary>
    public static class RarityPalette
    {
        public static Color Of(EnumService.Rarity rarity)
        {
            switch (rarity)
            {
                case EnumService.Rarity.Common:    return new Color32(0xB0, 0xB8, 0xC0, 0xFF); // серый
                case EnumService.Rarity.Rare:      return new Color32(0x3B, 0x9C, 0xE8, 0xFF); // синий
                case EnumService.Rarity.Epic:      return new Color32(0xA5, 0x5E, 0xE8, 0xFF); // фиолетовый
                case EnumService.Rarity.Legendary: return new Color32(0xF2, 0xB0, 0x33, 0xFF); // золотой
                case EnumService.Rarity.Exotic:    return new Color32(0xE8, 0x50, 0x6B, 0xFF); // красный
                default:                            return Color.white;
            }
        }
    }
}
