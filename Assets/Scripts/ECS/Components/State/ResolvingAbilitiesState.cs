namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Вешается на сущность игрока пока его ход обрабатывает очередь способностей.
    /// Блокирует ввод.
    /// </summary>
    public struct ResolvingAbilitiesState
    {
        public int CardEntity;      // карта, чья очередь способностей выполняется
        public int AbilityIndex;    // индекс текущей способности в AbilityContainerComponent
    }
}
