using Game.Core.Shared.Interface;
using Game.Core.Ecs.Components;
using Leopotam.EcsLite;

namespace Game.Core.Model.Effect
{
    /// <summary>
    /// Вернуть существо в руку владельца. Применяется ApplyReturnToHandSystem.
    /// Эквивалент «Призвать ураган», «Скелетон-возврат» и т.п.
    /// </summary>
    public class ReturnToHandEffect : AbilityEffect
    {
        public ReturnToHandEffect() { }
        private ReturnToHandEffect(ReturnToHandEffect _) { }

        public override void AddEffect(EcsWorld world, int effectEntity)
        {
            var pool = world.GetPool<ReturnToHandEffectComponent>();
            if (!pool.Has(effectEntity)) pool.Add(effectEntity);
        }

        public override IAbilityEffect Clone() => new ReturnToHandEffect(this);
    }
}
