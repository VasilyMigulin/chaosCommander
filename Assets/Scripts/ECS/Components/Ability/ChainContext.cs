namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Контекст текущей применяемой стадии цепочки (скрэтч). RunChainSystem кладёт сюда накопленный
    /// KilledCount ПЕРЕД применением эффектов стадии, а эффекты с CountSource=ChainKilled читают его
    /// (эффект не знает ability-сущность, поэтому передаём через статик, как SummonScratch).
    /// ECS однопоточный, стадия применяется по одной за тик → реентерабельности нет.
    /// </summary>
    public static class ChainContext
    {
        public static int CurrentKilled;
    }
}
