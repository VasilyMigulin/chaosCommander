namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// ECS-ивент: игрок хочет разыграть карту с руки.
    /// Создаётся CardInputSystem из GameEventBus CardPlayRequestedEvent.
    /// Singleton-entity (один за кадр).
    /// </summary>
    public struct CardPlayRequestEvent
    {
        public int CardEntity;
        public int PlayerEntity;
    }
}
