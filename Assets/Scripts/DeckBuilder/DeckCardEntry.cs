using Game.Core.Model.Card;

namespace Game.Core.DeckBuilder
{
    /// <summary>
    /// Рантайм-запись о карте, добавленной в текущую строящуюся колоду.
    /// DeckCount — сколько копий уже в колоде.
    /// </summary>
    public class DeckCardEntry
    {
        public readonly CardModel Model;

        public int DeckCount { get; private set; }

        public DeckCardEntry(CardModel model, int deckCount)
        {
            Model     = model;
            DeckCount = deckCount;
        }

        public void Increment() => DeckCount++;
        public void Decrement() => DeckCount = DeckCount > 0 ? DeckCount - 1 : 0;
    }
}
