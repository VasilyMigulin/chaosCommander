namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Вешается на entity способности если она применяется ко всем подходящим существам/картам на поле.
    /// RunResolveAbilityFieldSystem расширяет один ResolveAbilityEvent в несколько.
    /// </summary>
    public struct FieldAbilityTag { }
}
