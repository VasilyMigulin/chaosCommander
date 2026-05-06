namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Вешается на entity способности когда все её условия выполнены.
    /// UI подписывается на AbilityReadyEvent из GameEventBus чтобы обновить вьюшку.
    /// </summary>
    public struct ReadyTag { }
}
