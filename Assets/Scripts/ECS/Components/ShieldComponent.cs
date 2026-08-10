namespace Game.Core.Ecs.Components
{
    // === struct (Component) ===
    /// <summary>
    /// Свойство «Укреплённый»: поглощает Charges ближайших входящих ударов ПОЛНОСТЬЮ (0 урона за раз, не
    /// разбивается на очки урона), после чего снимается сам. Проверяется в TakeDamageSystem.ApplyDamage —
    /// единая точка урона (и бой через AttackHitEvent, и урон от способностей через TakeDamageEvent).
    /// </summary>
    public struct ShieldComponent
    {
        public int Charges;
    }
}
