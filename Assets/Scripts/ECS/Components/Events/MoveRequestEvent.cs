namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Запрос хода существа на клетку. Вешается на сущность существа.
    /// </summary>
    public struct MoveRequestEvent
    {
        public int ToRow;
        public int ToCol;
    }
}
