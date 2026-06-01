using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Service;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Применяет AddColorEffectComponent и RemoveColorEffectComponent: дёргает
    /// соответствующие цветовые теги (RedTag/BlueTag/...) на TargetEntity.
    /// </summary>
    public sealed class ApplyColorMutationSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;

        readonly EcsFilterInject<Inc<HitComponent, AddColorEffectComponent, TargetEntityComponent>> _addFilter = default;
        readonly EcsFilterInject<Inc<HitComponent, RemoveColorEffectComponent, TargetEntityComponent>> _remFilter = default;

        readonly EcsPoolInject<AddColorEffectComponent> _addPool = default;
        readonly EcsPoolInject<RemoveColorEffectComponent> _remPool = default;
        readonly EcsPoolInject<TargetEntityComponent> _targetPool = default;

        readonly EcsPoolInject<RedTag>    _red    = default;
        readonly EcsPoolInject<BlueTag>   _blue   = default;
        readonly EcsPoolInject<GreenTag>  _green  = default;
        readonly EcsPoolInject<YellowTag> _yellow = default;
        readonly EcsPoolInject<WhiteTag>  _white  = default;
        readonly EcsPoolInject<BlackTag>  _black  = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var effectEntity in _addFilter.Value)
            {
                int target = _targetPool.Value.Get(effectEntity).TargetEntity;
                if (target >= 0)
                    Mutate(target, _addPool.Value.Get(effectEntity).Colors, add: true);
                _world.Value.DelEntity(effectEntity);
            }

            foreach (var effectEntity in _remFilter.Value)
            {
                int target = _targetPool.Value.Get(effectEntity).TargetEntity;
                if (target >= 0)
                    Mutate(target, _remPool.Value.Get(effectEntity).Colors, add: false);
                _world.Value.DelEntity(effectEntity);
            }
        }

        void Mutate(int entity, EnumService.Element colors, bool add)
        {
            Toggle(entity, _red,    colors, EnumService.Element.Red,    add);
            Toggle(entity, _blue,   colors, EnumService.Element.Blue,   add);
            Toggle(entity, _green,  colors, EnumService.Element.Green,  add);
            Toggle(entity, _yellow, colors, EnumService.Element.Yellow, add);
            Toggle(entity, _white,  colors, EnumService.Element.White,  add);
            Toggle(entity, _black,  colors, EnumService.Element.Black,  add);
        }

        static void Toggle<T>(int entity, EcsPoolInject<T> pool, EnumService.Element flags,
                              EnumService.Element bit, bool add) where T : struct
        {
            if ((flags & bit) == 0) return;
            bool has = pool.Value.Has(entity);
            if (add && !has) pool.Value.Add(entity);
            else if (!add && has) pool.Value.Del(entity);
        }
    }
}
