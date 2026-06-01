namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Эффект-производитель: предложить игроку выбрать карту из источника.
    /// Применяется ApplyPickCardSystem:
    ///   • Свои карты — публикует CardPickOfferedEvent, ждёт CardPickChosenEvent.
    ///   • Вражеские (replay) — ждёт запись в CardPickReplayStore.
    /// Когда выбор готов: пишет entity выбранной карты в ChainStateComponent.ProducedEntity
    /// (на своей стороне), удаляет effect-entity и публикует CardPickResolvedNetEvent
    /// для синхронизации.
    /// </summary>
    public struct PickCardEffectComponent
    {
        public CardPickSourceType Source;
        public int OfferCount;
        public int[] UniquePoolModelIds;
    }
}
