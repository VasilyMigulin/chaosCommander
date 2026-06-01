namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Разыграть карту, лежащую в TargetEntity (например, выбранную раскопкой).
    /// ApplyCastTargetCardSystem вешает CastEvent на TargetEntity с OwnerEntity =
    /// owner текущего каста.
    /// </summary>
    public struct CastTargetCardEffectComponent { }
}
