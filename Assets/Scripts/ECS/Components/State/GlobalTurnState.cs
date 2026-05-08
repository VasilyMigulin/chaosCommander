namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Singleton-компонент: вешается на одну выделенную сущность (GlobalTurnEntity).
    /// Хранит глобальное состояние хода, которое одинаково на обоих клиентах.
    /// Устанавливается только по команде хоста через RPC.
    /// </summary>
    public struct GlobalTurnState
    {
        public int ActivePlayerId;
        public int TurnNumber;
        public int PersonalTurnNumber;
    }
}
