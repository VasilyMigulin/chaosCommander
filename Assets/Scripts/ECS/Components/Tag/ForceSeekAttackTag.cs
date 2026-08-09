namespace Game.Core.Ecs.Components
{
    // === struct (Tag) ===
    /// <summary>
    /// «Броситься в атаку» (Позвать стражу): существо должно само найти ближайшего вражеского существа,
    /// дойти до него и ударить — бесплатно по скорости, сверх лимита атак за ход. Ставит ForceAttackEffect
    /// (Ability-сборка, без доступа к BoardNav/PathMoveComponent-исполнению), разбирает ForceSeekAttackSystem
    /// (Ecs.Systems-сборка) — тот же паттерн «эффект помечает намерение, система считает путь».
    /// </summary>
    public struct ForceSeekAttackTag { }
}
