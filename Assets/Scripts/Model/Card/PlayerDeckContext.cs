using System.Collections.Generic;

namespace Game.Core.Model.Card
{
    /// <summary>
    /// Хранит выбранную игроком колоду до начала боя.
    /// Заполняется на экране выбора колоды или лобби, до загрузки боевой сцены.
    /// ECS слой читает данные через DeckInitializer — ссылка на CardInstanceData в ECS не попадает.
    /// </summary>
    public static class PlayerDeckContext
    {
        static readonly List<CardModel> _deck = new List<CardModel>();

        public static IReadOnlyList<CardModel> Deck => _deck;

        public static void Set(IEnumerable<CardModel> cards)
        {
            _deck.Clear();
            _deck.AddRange(cards);
        }

        public static void Clear() => _deck.Clear();
    }
}
