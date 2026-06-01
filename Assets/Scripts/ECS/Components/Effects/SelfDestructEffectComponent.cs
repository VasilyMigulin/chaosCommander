namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Источник способности умирает (DeadTag). Используется Всадниками
    /// и подобными «При разыгрывании: умирает» картами — за смертью отрабатывают
    /// штатные OnDie-триггеры (вторая способность того же существа).
    /// </summary>
    public struct SelfDestructEffectComponent { }
}
