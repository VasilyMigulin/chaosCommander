using Game.Core.Model.Card;

namespace Game.Core.DeckBuilder
{
    /// <summary>
    /// Рантайм-запись о карте в библиотеке игрока.
    /// OwnedCount — сколько копий игрок имеет.
    /// </summary>
    public class CardEntry
    {
        public readonly CardModel Model;

        public int OwnedCount { get; private set; }

        public CardEntry(CardModel model, int ownedCount)
        {
            Model      = model;
            OwnedCount = ownedCount;
        }

        public void AddCopies(int amount)
        {
            OwnedCount += amount;
        }
    }
}
