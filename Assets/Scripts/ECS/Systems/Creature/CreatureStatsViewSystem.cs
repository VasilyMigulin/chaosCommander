using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Mono;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Пушит статы существ (HP/атака/скорость) из ECS в их CreatureView каждый кадр.
    /// CreatureView.SetStats сам сравнивает значения и обновляет текст только при изменении
    /// (+ подсветка при потере HP), поэтому покадровый вызов дёшев и всегда отражает актуальное
    /// состояние — урон/лечение/баффы/трату скорости — без отдельных событий на каждое изменение.
    /// На доске нет другого отображения статов, поэтому единый push-источник проще и надёжнее.
    /// </summary>
    public sealed class CreatureStatsViewSystem : IEcsRunSystem
    {
        readonly EcsFilterInject<Inc<CreatureTag, ViewRefComponent, HealthComponent, AttackComponent, SpeedComponent>> _filter = default;
        readonly EcsPoolInject<ViewRefComponent> _viewPool = default;
        readonly EcsPoolInject<HealthComponent>  _hpPool   = default;
        readonly EcsPoolInject<AttackComponent>  _atkPool  = default;
        readonly EcsPoolInject<SpeedComponent>   _spPool   = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var e in _filter.Value)
            {
                ref var vr = ref _viewPool.Value.Get(e);
                if (vr.View == null || !vr.View.activeSelf) continue;   // не заспавнен / погиб (скрыт)

                var view = vr.View.GetComponent<CreatureView>();
                if (view == null) continue;

                view.SetStats(
                    _hpPool.Value.Get(e).Current,
                    _atkPool.Value.Get(e).Value,
                    _spPool.Value.Get(e).Remaining);
            }
        }
    }
}
