using Game.Core.Shared.Interface;
using Game.Core.Ecs.Components;
using Leopotam.EcsLite;

namespace Game.Core.Model.Effect
{
    /// <summary>«Цель умрёт через N своих ходов» (Харизматичный священник).</summary>
    public class AddCreatureTimerEffect : AbilityEffect
    {
        public int Turns = 3;

        public AddCreatureTimerEffect() { }
        public AddCreatureTimerEffect(int turns) { Turns = turns; }
        private AddCreatureTimerEffect(AddCreatureTimerEffect s) { Turns = s.Turns; }

        public override void AddEffect(EcsWorld world, int effectEntity)
        {
            var pool = world.GetPool<AddCreatureTimerEffectComponent>();
            if (!pool.Has(effectEntity)) { ref var c = ref pool.Add(effectEntity); c.Turns = Turns; }
        }

        public override IAbilityEffect Clone() => new AddCreatureTimerEffect(this);
    }
}
