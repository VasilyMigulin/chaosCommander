namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Контекст резолва ТЕКУЩЕЙ способности (скрэтч, как ChainContext/SummonScratch). RunResolveAbilityQueue
    /// System кладёт сюда инициатора (AbilityOriginComponent ability-сущности) ПЕРЕД применением её эффектов,
    /// а эффекты генерации (GenerateCardEffect.Spawn*) читают его для АТРИБУЦИИ «кем замешано» — эффект не
    /// знает ability-сущность, поэтому передаём через статик. -1 = нет гранта (нативная способность → инициатор
    /// = владелец карты). ECS однопоточный, одна способность за тик → реентерабельности нет.
    /// </summary>
    public static class AbilityResolveContext
    {
        public static int OriginOwnerId = -1;

        public static void Clear() => OriginOwnerId = -1;
    }
}
