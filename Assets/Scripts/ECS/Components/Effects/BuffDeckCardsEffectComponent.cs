namespace Game.Core.Ecs.Components
{
    [System.Flags]
    public enum BuffZone
    {
        Deck  = 1 << 0,
        Hand  = 1 << 1,
        Grave = 1 << 2,
        Board = 1 << 3, // если включён, BuffStatsEffect уже накроет — можно для удобства
    }

    /// <summary>
    /// Перманентный бафф статов всем картам владельца цели в указанных зонах.
    /// Пишет в Base/BaseMax (AuraRecalc корректно учтёт при появлении карты на доске).
    /// Используется «Молитва о здравии» (+2 HP), «Патриарх» (+1/1).
    /// </summary>
    public struct BuffDeckCardsEffectComponent
    {
        public BuffZone Zones;
        public int AttackBonus;
        public int HealthBonus;
        public int SpeedBonus;
    }
}
