namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Передаёт контроль над TargetEntity владельцу источника способности.
    /// Меняет OwnerComponent.OwnerId, BoardPositionComponent.OwnerId, своп тегов
    /// OwnCardTag/EnemyCardTag. Визуал переспавнивается через SpawnCreatureViewSystem.
    /// </summary>
    public struct TakeControlEffectComponent { }
}
