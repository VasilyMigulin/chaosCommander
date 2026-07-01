namespace Game.Core.Shared.Interface
{
    /// <summary>
    /// Минимальная поверхность способности, видимая ECS-системам через AbilityRefComponent
    /// (который лежит в Game.Core.Ecs.Components). Благодаря этому интерфейсу Components
    /// не зависит от сборки поведения Game.Core.Ability — только от Shared.Interface.
    /// Конкретная реализация — Game.Core.Ability.Ability.
    /// </summary>
    public interface IAbility
    {
        void Dispose();
    }
}
