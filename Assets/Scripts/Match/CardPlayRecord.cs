using Game.Core.Service;

namespace Game.Core.Match
{
    /// <summary>
    /// Запись о карте, сыгранной в течение матча.
    /// Создаётся в момент розыгрыша и никогда не изменяется.
    /// </summary>
    public sealed class CardPlayRecord
    {
        /// <summary>ECS entity карты.</summary>
        public readonly int CardEntity;

        /// <summary>Model.Id карты (постоянный идентификатор из дизайна).</summary>
        public readonly int ModelId;

        /// <summary>Имя карты (Model.Name).</summary>
        public readonly string CardName;

        /// <summary>Id игрока, который разыграл карту.</summary>
        public readonly int PlayerId;

        /// <summary>Редкость.</summary>
        public readonly EnumService.Rarity Rarity;

        /// <summary>Элемент.</summary>
        public readonly EnumService.Element Element;

        /// <summary>Тип карты.</summary>
        public readonly EnumService.CardType Type;

        /// <summary>Номер хода в котором разыграна карта.</summary>
        public readonly int TurnNumber;

        public CardPlayRecord(
            int cardEntity,
            int modelId,
            string cardName,
            int playerId,
            EnumService.Rarity rarity,
            EnumService.Element element,
            EnumService.CardType type,
            int turnNumber)
        {
            CardEntity = cardEntity;
            ModelId    = modelId;
            CardName   = cardName;
            PlayerId   = playerId;
            Rarity     = rarity;
            Element    = element;
            Type       = type;
            TurnNumber = turnNumber;
        }
    }
}
