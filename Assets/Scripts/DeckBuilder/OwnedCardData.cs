using System;

namespace Game.Core.DeckBuilder
{
    /// <summary>
    /// Персистентные данные о владении картой.
    /// Сохраняется в PlayerPrefs в виде JSON.
    /// </summary>
    [Serializable]
    public struct OwnedCardData
    {
        public string ExpansionId;
        public int    CardId;
        public int    Count;

        public OwnedCardData(string expansionId, int cardId, int count)
        {
            ExpansionId = expansionId;
            CardId      = cardId;
            Count       = count;
        }
    }
}
