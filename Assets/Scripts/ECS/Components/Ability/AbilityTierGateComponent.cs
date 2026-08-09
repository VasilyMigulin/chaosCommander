namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// На ABILITY-сущности: эта способность принадлежит уровню Tier карты-с-уровнями (индекс в CardModel.Tiers).
    /// Все способности всех уровней инитятся сразу, но AbilityFire.Mark срабатывает только если текущий уровень
    /// карты (CardTierComponent.CurrentTier) == Tier. Способности без этого компонента — общие (всегда активны).
    /// </summary>
    public struct AbilityTierGateComponent
    {
        public int Tier;
    }
}
