namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Возвращает TargetEntity (карту/существо на доске) в руку её владельца.
    /// Снимает BoardTag/GraveTag/BoardPosition, добавляет HandTag, кладёт в HandComponent.
    /// </summary>
    public struct ReturnToHandEffectComponent { }
}
