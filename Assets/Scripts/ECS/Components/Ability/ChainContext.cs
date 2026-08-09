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

        /// <summary>Стоимость карты, СБРОШЕННОЙ на прошлой стадии цепочки (DiscardEffect кладёт сюда). Следующая
        /// стадия читает через RepeatEffect{Source=ChainDiscardedCost} (Утилизация: урон = стоимости сброшенной).</summary>
        public static int LastDiscardedCost;
    }
}
