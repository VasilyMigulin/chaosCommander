using Game.Core.Shared.Interface;
using Game.Core.Ecs.Components;
using Leopotam.EcsLite;

namespace Game.Core.Model.Effect
{
    /// <summary>
    /// Динамический бафф: статы = PerCount × счётчик сыгранных карт с CounterModelId
    /// у владельца источника способности. Используется «Грыз».
    /// </summary>
    public class BuffByCounterEffect : AbilityEffect
    {
        public int CounterModelId;
        public int AttackPerCount;
        public int HealthPerCount;

        public BuffByCounterEffect() { }
        public BuffByCounterEffect(int counterModelId, int atkPer, int hpPer)
        {
            CounterModelId = counterModelId; AttackPerCount = atkPer; HealthPerCount = hpPer;
        }

        private BuffByCounterEffect(BuffByCounterEffect source)
        {
            CounterModelId = source.CounterModelId;
            AttackPerCount = source.AttackPerCount;
            HealthPerCount = source.HealthPerCount;
        }

        public override void AddEffect(EcsWorld world, int effectEntity)
        {
            var pool = world.GetPool<BuffByCounterEffectComponent>();
            if (!pool.Has(effectEntity))
            {
                ref var c = ref pool.Add(effectEntity);
                c.CounterModelId = CounterModelId;
                c.AttackPerCount = AttackPerCount;
                c.HealthPerCount = HealthPerCount;
            }
        }

        public override IAbilityEffect Clone() => new BuffByCounterEffect(this);
    }
}
