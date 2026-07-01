namespace Game.Core.Ecs.Components
{
    // === struct (Component) ===
    /// <summary>
    /// Сколько ещё раз должна разрешиться способность (множитель частоты — Временная петля и т.п.). Ставит
    /// AbilityFire.Mark на ability-сущности, если множитель > 1; RunResolveAbilityQueueSystem повторяет
    /// резолв и декрементит. Только на активе (пассив получает N отдельных ActionAbilityData и реплеит их).
    /// </summary>
    public struct PendingCastsComponent
    {
        public int Remaining;
    }
}
