namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Переместить карту в кладбище. Снимает HandTag/BoardTag/DeckTag,
    /// удаляет из HandComponent владельца, ставит GraveTag.
    /// Токены — удаляются полностью.
    /// </summary>
    public struct MoveCardToGraveEvent { }
}
