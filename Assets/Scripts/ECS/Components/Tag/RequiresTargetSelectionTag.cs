namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Вешается на entity КАРТЫ когда для её разыгрывания нужен явный выбор цели
    /// (вражеского существа, клетки и т.д.) игроком перед кастом.
    /// CastCardSystem не запустится пока этот тег присутствует.
    /// Снимается в TargetSelectionSystem как только игрок кликнул по валидной цели.
    /// </summary>
    public struct RequiresTargetSelectionTag { }
}
