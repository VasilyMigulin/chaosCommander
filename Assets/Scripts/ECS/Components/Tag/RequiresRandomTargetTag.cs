namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Вешается на entity КАРТЫ когда для её разыгрывания нужна случайная цель
    /// (без участия игрока). RandomTargetSystem найдёт подходящее существо,
    /// создаст CastEvent и снимет этот тег.
    /// CastCardSystem не запустится пока этот тег присутствует.
    /// </summary>
    public struct RequiresRandomTargetTag { }
}
