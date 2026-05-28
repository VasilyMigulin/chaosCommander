using Game.Core.Shared.Interface;
using Game.Core.Ecs.Components;
using Leopotam.EcsLite;

namespace Game.Core.Model.Effect
{
    /// <summary>
    /// Discard: цель сбрасывает N случайных карт из руки.
    /// Цель должна быть entity игрока (с HandComponent).
    /// </summary>
    public class DiscardEffect : AbilityEffect
    {
        public int Count;

        public DiscardEffect() { }

        public DiscardEffect(int count)
        {
            Count = count;
        }

        private DiscardEffect(DiscardEffect source)
        {
            Count = source.Count;
        }

        public override void AddEffect(EcsWorld world, int effectEntity)
        {
            var pool = world.GetPool<DiscardEffectComponent>();
            if (!pool.Has(effectEntity))
            {
                ref var comp = ref pool.Add(effectEntity);
                comp.Count = Count;
            }
            else
            {
                pool.Get(effectEntity).Count += Count;
            }
        }

        public override IAbilityEffect Clone() => new DiscardEffect(this);
    }
}
