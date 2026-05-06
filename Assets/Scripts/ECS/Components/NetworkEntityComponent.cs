namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Уникальный строковый ключ сущности для синхронизации между клиентами.
    /// Генерируется при сборке колоды перед началом матча.
    /// </summary>
    public struct NetworkEntityComponent
    {
        public string NetworkEntityKey;
    }
}
