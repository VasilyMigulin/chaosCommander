using Game.Core.Shared.Interface;
using Game.Core.Ecs.Components;
using Leopotam.EcsLite;

namespace Game.Core.Model.Effect
{
    /// <summary>
    /// Целевой игрок добирает карты.
    /// Цель должна быть entity игрока (с PlayerComponent + DeckComponent + HandComponent).
    /// </summary>
    public class DrawEffect : AbilityEffect
    {
        public int Count;

        public DrawEffect() { }

        public DrawEffect(int count)
        {
            Count = count;
        }

        private DrawEffect(DrawEffect source)
        {
            Count = source.Count;
        }

        public override void AddEffect(EcsWorld world, int effectEntity)
        {
            var pool = world.GetPool<DrawEffectComponent>();
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

        public override IAbilityEffect Clone() => new DrawEffect(this);
    }
}
