namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Снимает Amount золота с TargetEntity (игрока). Минимум — 0.
    /// </summary>
    public struct LoseGoldEffectComponent
    {
        public int Amount;
    }
}
