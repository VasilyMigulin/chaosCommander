using Game.Core.Shared.Interface;
using Game.Core.Ecs.Components;
using Leopotam.EcsLite;

namespace Game.Core.Model.Effect
{
    /// <summary>
    /// Уничтожить цель (существо) без расчёта урона. Применяется ApplyDestroySystem:
    /// сохраняет BoardPosition цели в ChainStateComponent.CapturedCell, ставит DeadTag —
    /// дальше работает DieSystem (вкл. предсмертные триггеры). Не имеет численных параметров.
    /// </summary>
    public class DestroyEffect : AbilityEffect
    {
        public DestroyEffect() { }
        private DestroyEffect(DestroyEffect _) { }

        public override void AddEffect(EcsWorld world, int effectEntity)
        {
            var pool = world.GetPool<DestroyEffectComponent>();
            if (!pool.Has(effectEntity))
                pool.Add(effectEntity);
        }

        public override IAbilityEffect Clone() => new DestroyEffect(this);
    }
}
