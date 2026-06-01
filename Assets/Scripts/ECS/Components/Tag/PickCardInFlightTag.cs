namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Маркер на effect-entity PickCardEffectComponent: UI-предложение уже опубликовано
    /// (свои) или ожидание стора началось (враги). ApplyPickCardSystem не дёргает Offer
    /// повторно, а на каждом кадре проверяет резолюцию.
    /// </summary>
    public struct PickCardInFlightTag { }
}
