namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// ECS-событие: игрок явно запрашивает конец своего хода.
    /// Создаётся на сущности игрока (или на пустой сущности — по нажатию кнопки / RPC).
    /// </summary>
    public struct EndTurnRequestEvent
    {
        public int RequestingPlayerId;
    }
}
