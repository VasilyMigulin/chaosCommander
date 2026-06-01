using Game.Core.Shared.Interface;
using Game.Core.Ecs.Components;
using Leopotam.EcsLite;

namespace Game.Core.Model.Effect
{
    /// <summary>
    /// «Источник умирает». Запускает DieSystem на карте-источнике — её OnDie-абилки
    /// сработают штатно (см. Всадник Разложения / Войны / Голода / Смерти).
    /// </summary>
    public class SelfDestructEffect : AbilityEffect
    {
        public SelfDestructEffect() { }
        private SelfDestructEffect(SelfDestructEffect _) { }

        public override void AddEffect(EcsWorld world, int effectEntity)
        {
            var pool = world.GetPool<SelfDestructEffectComponent>();
            if (!pool.Has(effectEntity)) pool.Add(effectEntity);
        }

        public override IAbilityEffect Clone() => new SelfDestructEffect(this);
    }
}
