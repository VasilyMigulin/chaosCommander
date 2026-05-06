using System;
using System.Collections.Generic;

namespace Game.Core.DeckBuilder
{
    /// <summary>
    /// Персистентные данные сохранённой колоды.
    /// Commander хранится отдельно — это легендарная карта-существо.
    /// </summary>
    [Serializable]
    public class SavedDeckData
    {
        public string         Name;
        public OwnedCardData  Commander;
        public List<OwnedCardData> Cards = new List<OwnedCardData>();
    }
}
