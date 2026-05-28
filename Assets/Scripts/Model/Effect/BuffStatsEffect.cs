using Game.Core.Shared.Interface;
using Game.Core.Ecs.Components;
using Leopotam.EcsLite;

namespace Game.Core.Model.Effect
{
    /// <summary>
    /// Бафф статов существа: +AttackBonus атаки, +HealthBonus здоровья.
    /// Цель должна быть entity существа (с AttackComponent + HealthComponent).
    /// </summary>
    public class BuffStatsEffect : AbilityEffect
    {
        public int AttackBonus;
        public int HealthBonus;

        public BuffStatsEffect() { }

        public BuffStatsEffect(int attackBonus, int healthBonus)
        {
            AttackBonus = attackBonus;
            HealthBonus = healthBonus;
        }

        private BuffStatsEffect(BuffStatsEffect source)
        {
            AttackBonus = source.AttackBonus;
            HealthBonus = source.HealthBonus;
        }

        public override void AddEffect(EcsWorld world, int effectEntity)
        {
            var pool = world.GetPool<BuffEffectComponent>();
            if (!pool.Has(effectEntity))
            {
                ref var comp = ref pool.Add(effectEntity);
                comp.AttackBonus = AttackBonus;
                comp.HealthBonus = HealthBonus;
            }
            else
            {
                ref var comp = ref pool.Get(effectEntity);
                comp.AttackBonus += AttackBonus;
                comp.HealthBonus += HealthBonus;
            }
        }

        public override IAbilityEffect Clone() => new BuffStatsEffect(this);
    }
}
