using Game.Core.Shared.Interface;
using Game.Core.Ecs.Components;
using Leopotam.EcsLite;

namespace Game.Core.Model.Effect
{
    /// <summary>
    /// Бафф статов: +AttackBonus, +HealthBonus, +SpeedBonus.
    /// Под аурой суммы попадают в AuraSourceComponent (AuraRecalcSystem пересчитывает эффективные статы).
    /// При прямом применении ApplyBuffSystem пишет в Base/BaseMax (перманентный бафф).
    /// </summary>
    public class BuffStatsEffect : AbilityEffect
    {
        public int AttackBonus;
        public int HealthBonus;
        public int SpeedBonus;

        public BuffStatsEffect() { }

        public BuffStatsEffect(int attackBonus, int healthBonus, int speedBonus = 0)
        {
            AttackBonus = attackBonus;
            HealthBonus = healthBonus;
            SpeedBonus  = speedBonus;
        }

        private BuffStatsEffect(BuffStatsEffect source)
        {
            AttackBonus = source.AttackBonus;
            HealthBonus = source.HealthBonus;
            SpeedBonus  = source.SpeedBonus;
        }

        public override void AddEffect(EcsWorld world, int effectEntity)
        {
            var pool = world.GetPool<BuffEffectComponent>();
            if (!pool.Has(effectEntity))
            {
                ref var comp = ref pool.Add(effectEntity);
                comp.AttackBonus = AttackBonus;
                comp.HealthBonus = HealthBonus;
                comp.SpeedBonus  = SpeedBonus;
            }
            else
            {
                ref var comp = ref pool.Get(effectEntity);
                comp.AttackBonus += AttackBonus;
                comp.HealthBonus += HealthBonus;
                comp.SpeedBonus  += SpeedBonus;
            }
        }

        public override IAbilityEffect Clone() => new BuffStatsEffect(this);
    }
}
