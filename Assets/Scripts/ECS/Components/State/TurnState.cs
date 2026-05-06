namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Вешается на сущность игрока, когда его ход. Снимается при передаче хода.
    /// </summary>
    public struct TurnState 
    {
        public int TurnNumber;          // глобальный счётчик ходов
        public int PersonalTurnNumber;  // сколько раз именно этот игрок ходил (для дохода золота)
        public float TimeRemaining;
    }
}