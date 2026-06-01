namespace Game.Core.Shared.Interface
{
    /// <summary>
    /// Чистый интерфейс предиката шага цепочки эффектов. Модель ChainCondition
    /// (Game.Core.Model.Ability) реализует его. Компонент AbilityChainContainerComponent
    /// держит ссылку только на интерфейс — слой данных не зависит от слоя моделей.
    /// </summary>
    public interface IChainCondition
    {
        bool Evaluate(Leopotam.EcsLite.EcsWorld world,
                      int abilityEntity,
                      int sourceCard,
                      int ownerPlayerId,
                      int ownerPlayerEntity);
    }
}
