namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Хранит данные колоды оппонента полученные по сети для создания локальных entity.
    /// Вешается на entity игрока оппонента.
    /// </summary>
    public struct OpponentDeckSyncEvent
    {
        public string[] DeckExpansionIds;
        public int[]    DeckCardIds;
        public string[] DeckNetworkKeys;
        public int      DeckCount;
        public string[] HandExpansionIds;
        public int[]    HandCardIds;
        public string[] HandNetworkKeys;
        public int      HandCount;
        public string CommanderExpansionID;
        public int    CommanderID;
        public string CommanderNetKey;

        // «Отложенные» (сайдборд Сказочника) — своя зона у зеркала, нужна чтобы ключи разрешались,
        // когда владелец достанет карту раскопкой.
        public string[] SideboardExpansionIds;
        public int[]    SideboardCardIds;
        public string[] SideboardNetworkKeys;
        public int      SideboardCount;
    }
}
