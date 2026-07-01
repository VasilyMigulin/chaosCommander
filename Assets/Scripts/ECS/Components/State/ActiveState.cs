namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Единственный маркер «сейчас ходит этот игрок». Вешается на сущность активного игрока
    /// (ровно один на клиенте), снимается при передаче хода. Само наличие = активен — на нём
    /// завязаны ВСЕ гейты розыгрыша (ввод, способности, таймер). Заменяет связку
    /// TurnState + TurnPhaseState + фазовую машину.
    /// </summary>
    public struct ActiveState
    {
        public int TurnNumber;          // глобальный счётчик ходов (для отображения)
        public int PersonalTurnNumber;  // сколько раз этот игрок ходил (для дохода золота)
        public float TimeRemaining;
    }
}
