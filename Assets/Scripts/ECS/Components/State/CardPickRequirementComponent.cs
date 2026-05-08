namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Тип источника, из которого собирается пул карт для выбора (раскопка).
    /// </summary>
    public enum CardPickSourceType
    {
        /// <summary>Верхние N карт колоды самого игрока.</summary>
        PlayerDeck    = 0,
        /// <summary>Верхние N карт колоды оппонента.</summary>
        OpponentDeck  = 1,
        /// <summary>Случайные карты из руки самого игрока.</summary>
        PlayerHand    = 2,
        /// <summary>Случайные карты из руки оппонента.</summary>
        OpponentHand  = 3,
        /// <summary>Случайные карты с кладбища самого игрока.</summary>
        PlayerGrave   = 4,
        /// <summary>Случайные карты с кладбища оппонента.</summary>
        OpponentGrave = 5,
        /// <summary>Явно заданный пул по ModelId (UniquePoolModelIds).</summary>
        UniquePool    = 6,
    }

    /// <summary>
    /// Вешается на entity КАРТЫ при инициализации через RequireCardPickPlayRequirement.
    /// Определяет откуда брать предложенные карты и сколько их показывать.
    /// CardPickSelectionSystem читает этот компонент когда на карте есть CastEvent +
    /// RequiresCardPickTag.
    /// </summary>
    public struct CardPickRequirementComponent
    {
        /// <summary>Источник карт для предложения.</summary>
        public CardPickSourceType Source;

        /// <summary>Сколько карт предложить игроку (обычно 3).</summary>
        public int OfferCount;

        /// <summary>
        /// Только для Source == UniquePool.
        /// Список ModelId карт из которых нужно случайно отобрать OfferCount штук.
        /// </summary>
        public int[] UniquePoolModelIds;
    }
}
