using Game.Core.Shared.Interface;
using Game.Core.Ecs.Components;
using Game.Core.Service;
using Leopotam.EcsLite;

namespace Game.Core.Model.Effect
{
    /// <summary>
    /// Добавить цвет(а) цели. Используется «Обращение в веру», «Великое обращение» (вне ауры — постоянная версия).
    /// </summary>
    public class AddColorEffect : AbilityEffect
    {
        public EnumService.Element Colors;

        public AddColorEffect() { }
        public AddColorEffect(EnumService.Element colors) { Colors = colors; }
        private AddColorEffect(AddColorEffect source) { Colors = source.Colors; }

        public override void AddEffect(EcsWorld world, int effectEntity)
        {
            var pool = world.GetPool<AddColorEffectComponent>();
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

        public override IAbilityEffect Clone() => new AddColorEffect(this);
    }
}
