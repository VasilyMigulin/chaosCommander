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
    }
}
