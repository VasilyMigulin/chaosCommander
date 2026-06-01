namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Наносит урон всем существам в указанной зоне (колода/рука/кладбище) владельца TargetEntity.
    /// Используется «Охотник за твоей головой» (1 урона всем существам в колоде оппонента).
    /// </summary>
    public struct DamageInZoneEffectComponent
    {
        public BuffZone Zones;
        public int Amount;
        public bool CreatureOnly;
    }
}
