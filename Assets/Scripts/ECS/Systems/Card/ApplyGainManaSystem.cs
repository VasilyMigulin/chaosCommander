using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Обрабатывает GainManaEffectComponent на effect entity.
    /// Применяет прирост маны к ManaComponent на TargetEntity и уведомляет UI.
    /// </summary>
    public sealed class ApplyGainManaSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsFilterInject<Inc<HitComponent, GainManaEffectComponent, TargetEntityComponent>> _filter = default;
        readonly EcsPoolInject<GainManaEffectComponent> _gainManaPool = default;
        readonly EcsPoolInject<TargetEntityComponent> _targetPool = default;
        readonly EcsPoolInject<ManaComponent> _manaPool = default;
        readonly EcsPoolInject<LocalComponent> _localPool = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var effectEntity in _filter.Value)
            {
                ref var effect = ref _gainManaPool.Value.Get(effectEntity);
                int targetEntity = _targetPool.Value.Get(effectEntity).TargetEntity;

                if (_manaPool.Value.Has(targetEntity))
                {
                    ref var mana = ref _manaPool.Value.Get(targetEntity);
                    // Мана — банк: накапливается без верхней границы. Max растёт по high-water,
                    // чтобы UI «X/Y» оставался осмысленным (Y — максимум когда-либо набранной маны).
                    mana.Current += effect.Amount;
                    if (mana.Current > mana.Max) mana.Max = mana.Current;

                    GameEventBus.Publish(new ResourceChangedEvent
                    {
                        isLocalPlayer = _localPool.Value.Has(targetEntity),
                        Type =  Service.EnumService.ResourceType.Mana,
                        NewValue = mana.Current,
                        MaxValue = mana.Max
                    });
                }

                _world.Value.DelEntity(effectEntity);
            }
        }
    }
}
