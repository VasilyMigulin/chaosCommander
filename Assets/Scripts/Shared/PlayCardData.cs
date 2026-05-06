using Game.Core.Service;
using UnityEngine;

namespace Game.Core.Shared
{
    /// <summary>
    /// Чистые данные карты для передачи в UI.
    /// Не содержит ECS-зависимостей, передаётся через GameEventBus.
    /// </summary>
    public struct PlayCardData
    {
        public int                       CardEntity;
        public string                    NetworkKey;
        public string                    CardName;
        public Sprite                    Icon;
        public EnumService.CardType      CardType;
        public EnumService.Element       Element;
        public EnumService.Rarity        Rarity;
        public bool                      IsCommander;

        /// <summary>Полный визуальный DTO — строится фабрикой из CardModel.</summary>
        public CardVisualData            Visual;
    }
}
