namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Запрос взятия карт из колоды в руку. Вешается на сущность игрока.
    /// Count — сколько карт взять (по умолчанию 1).
    /// </summary>
    public struct DrawCardEvent
    {
        public int Count;
    }
}
