namespace Game.Core.Model.Card
{
    /// <summary>
    /// Контракт реестра карт. Позволяет DeckInitializer не зависеть от сборки Game.Core.Configs.
    /// </summary>
    public interface ICardRegistry
    {
        CardModel Get(int modelId);
    }
}
