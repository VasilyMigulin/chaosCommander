namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// «Уничтожить» цель: ApplyDestroySystem перед смертью копирует BoardPosition цели
    /// в ChainStateComponent.CapturedCell (если есть EffectAbilityRefComponent) и вешает
    /// DeadTag — дальше DieSystem отрабатывает как обычно.
    /// </summary>
    public struct DestroyEffectComponent { }
}
