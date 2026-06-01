using Game.Core.Shared.Interface;
using Game.Core.Ecs.Components;
using Leopotam.EcsLite;

namespace Game.Core.Model.Effect
{
    /// <summary>Урон всем картам/существам в зонах владельца цели. Охотник за твоей головой.</summary>
    public class DamageInZoneEffect : AbilityEffect
    {
        public BuffZone Zones = BuffZone.Deck;
        public int Amount = 1;
        public bool CreatureOnly = true;

        public DamageInZoneEffect() { }
        private DamageInZoneEffect(DamageInZoneEffect s)
        {
            Zones = s.Zones; Amount = s.Amount; CreatureOnly = s.CreatureOnly;
        }

        public override void AddEffect(EcsWorld world, int effectEntity)
        {
            var pool = world.GetPool<DamageInZoneEffectComponent>();
            if (!pool.Has(effectEntity))
            {
                ref var c = ref pool.Add(effectEntity);
                c.Zones = Zones; c.Amount = Amount; c.CreatureOnly = CreatureOnly;
            }
        }

        public override IAbilityEffect Clone() => new DamageInZoneEffect(this);
    }
}
