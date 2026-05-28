using Game.Core.Shared.Interface;
using Game.Core.Ecs.Components;
using Leopotam.EcsLite;

namespace Game.Core.Model.Effect
{
    /// <summary>
    /// Mill: верхние N карт колоды цели отправляются в кладбище.
    /// Цель должна быть entity игрока (с DeckComponent).
    /// </summary>
    public class MillEffect : AbilityEffect
    {
        public int Count;

        public MillEffect() { }

        public MillEffect(int count)
        {
            Count = count;
        }

        private MillEffect(MillEffect source)
        {
            Count = source.Count;
        }

        public override void AddEffect(EcsWorld world, int effectEntity)
        {
            var pool = world.GetPool<MillEffectComponent>();
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

        public override IAbilityEffect Clone() => new MillEffect(this);
    }
}
