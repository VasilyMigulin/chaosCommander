using Game.Core.Shared.Interface;
using Game.Core.Ecs.Components;
using Leopotam.EcsLite;

namespace Game.Core.Model.Effect
{
    /// <summary>Даёт копию последнего разыгранного владельцем заклинания в его руку.</summary>
    public class GiveLastPlayedSpellToHandEffect : AbilityEffect
    {
        public GiveLastPlayedSpellToHandEffect() { }
        private GiveLastPlayedSpellToHandEffect(GiveLastPlayedSpellToHandEffect _) { }

        public override void AddEffect(EcsWorld world, int effectEntity)
        {
            var pool = world.GetPool<GiveLastPlayedSpellToHandEffectComponent>();
            if (!pool.Has(effectEntity)) pool.Add(effectEntity);
        }

        public override IAbilityEffect Clone() => new GiveLastPlayedSpellToHandEffect(this);
    }
}
