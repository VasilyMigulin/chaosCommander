using Game.Core.Shared.Interface;
using Game.Core.Ecs.Components;
using Game.Core.Service;
using Leopotam.EcsLite;

namespace Game.Core.Model.Effect
{
    /// <summary>Снять цвет(а) с цели. Используется «Отлучение».</summary>
    public class RemoveColorEffect : AbilityEffect
    {
        public EnumService.Element Colors;

        public RemoveColorEffect() { }
        public RemoveColorEffect(EnumService.Element colors) { Colors = colors; }
        private RemoveColorEffect(RemoveColorEffect source) { Colors = source.Colors; }

        public override void AddEffect(EcsWorld world, int effectEntity)
        {
            var pool = world.GetPool<RemoveColorEffectComponent>();
            if (!pool.Has(effectEntity))
            {
                ref var c = ref pool.Add(effectEntity);
                c.Colors = Colors;
            }
            else
            {
                pool.Get(effectEntity).Colors |= Colors;
            }
        }

        public override IAbilityEffect Clone() => new RemoveColorEffect(this);
    }
}
