namespace Game.Core.Shared.Interface
{
    /// <summary>
    /// Помечает данные карты (CardInstanceData), из которых можно породить НОВУЮ сущность.
    /// Живёт в Game.Core.Shared.Interface, чтобы Game.Core.Ability ссылался на него БЕЗ цикла
    /// (Model.Card → Ability уже есть; обратная прямая ссылка Ability → Instance.Card невозможна).
    ///
    /// Эффекты-генераторы (SummonGenerated/CreateCardToHand/ShuffleIntoDeck/Discover-пулы) держат
    /// ссылку на ассет через этот интерфейс и спавнят по идентичности — публикуют CreateCardEvent
    /// с ExpansionId+CardId (путь через CardConfig, sync-safe, как у карт колоды). Прямой init из
    /// ассета (вне CardConfig, для внешних пулов) добавим вместе с отдельным RPC синка.
    /// </summary>
    public interface ICreatable
    {
        string ExpansionId { get; }
        int CardId { get; }
    }
}
