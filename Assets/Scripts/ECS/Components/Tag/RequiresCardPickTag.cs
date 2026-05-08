namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Вешается на entity КАРТЫ когда для её разыгрывания нужен выбор из предложенных карт.
    /// CastCardSystem не запустится пока этот тег присутствует.
    /// Снимается в CardPickSelectionSystem как только игрок сделал выбор.
    /// </summary>
    public struct RequiresCardPickTag { }
}
