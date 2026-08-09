namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Запрос хода существа на клетку. Вешается на сущность существа.
    /// </summary>
    public struct MoveRequestEvent
    {
        public int ToRow;
        public int ToCol;
        public int ToOwnerId;

        /// <summary>Бесплатный ход (Позвать стражу и т.п.): MoveSystem не проверяет и не тратит SpeedComponent.Remaining.</summary>
        public bool Free;
    }
}
