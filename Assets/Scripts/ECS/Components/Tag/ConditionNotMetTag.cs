namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Вешается на entity resolve-события способности когда её condition (ReadyTag) не выполнен.
    /// RunResolveAbilityEffectSystem пропускает такие entity — способность отыгрывается,
    /// но эффекты не применяются.
    /// </summary>
    public struct ConditionNotMetTag { }
}
