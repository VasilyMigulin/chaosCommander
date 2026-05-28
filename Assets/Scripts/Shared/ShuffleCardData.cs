using Game.Core.Shared.Interface;

namespace Game.Core.Shared
{
    public struct ShuffleCardData
    {
        public IShuffled CardToShuffle;
        public int ShuffleCount;
        public bool IntoOpponentDeck;
    }
}