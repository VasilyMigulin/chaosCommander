using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Mono;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Обрабатывает AttackRequestEvent на существе:
    ///   1. Проверяет SpeedComponent.Remaining > 0.
    ///   2. Тратит 1 заряд.
    ///   3. Навешивает AttackAnimPendingTag (блокирует ввод и очередь).
    ///   4. Запускает анимацию через CreatureView.
    ///      - onHit коллбэк → создаёт AttackHitEvent (фиксирует удар).
    ///      - onFinished коллбэк → снимает AttackAnimPendingTag.
    /// </summary>
    public sealed class AttackSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;

        readonly EcsFilterInject<
            Inc<AttackRequestEvent, CreatureTag, BoardTag, SpeedComponent>,
            Exc<AttackAnimPendingTag, DeadTag>> _filter = default;

        readonly EcsPoolInject<AttackRequestEvent> _attackPool = default;
        readonly EcsPoolInject<SpeedComponent> _speedPool = default;
        readonly EcsPoolInject<AttackComponent> _atkValuePool = default;
        readonly EcsPoolInject<AttackAnimPendingTag> _animPendingPool = default;
        readonly EcsPoolInject<AttackHitEvent> _hitPool = default;
        readonly EcsPoolInject<ViewRefComponent> _viewPool = default;
        readonly EcsPoolInject<DeadTag> _deadPool = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var entity in _filter.Value)
            {
                ref var req   = ref _attackPool.Value.Get(entity);
                ref var speed = ref _speedPool.Value.Get(entity);

                if (speed.Remaining <= 0)
                {
                    _attackPool.Value.Del(entity);
                    continue;
                }

                int targetEntity = req.TargetEntity;
                int attackValue  = _atkValuePool.Value.Has(entity) ? _atkValuePool.Value.Get(entity).Value : 0;

                speed.Remaining--;
                _attackPool.Value.Del(entity);

                // Блокируем ввод/очередь на время анимации
                _animPendingPool.Value.Add(entity);
                GameEventBus.Publish(new InputBlockedEvent());

                // Захватываем entity для замыкания
                int attackerEntity = entity;
                var world          = _world.Value;
                var hitPool        = _hitPool.Value;
                var animPool       = _animPendingPool.Value;

                GameEventBus.Publish(new CreatureAttackedEvent
                {
                    AttackerEntity = attackerEntity,
                    DefenderEntity = targetEntity,
                });

                // Запускаем анимацию
                CreatureView view = GetView(entity);
                if (view != null)
                {
                    view.PlayAttack(
                        onHit: () =>
                        {
                            // Момент удара: фиксируем урон
                            if (_deadPool.Value.Has(attackerEntity) && _deadPool.Value.Has(targetEntity))
                            {
                                if (!hitPool.Has(attackerEntity))
                                {
                                    ref var hit = ref hitPool.Add(attackerEntity);
                                    hit.TargetEntity = targetEntity;
                                    hit.Amount       = attackValue;
                                }
                            }
                        },
                        onFinished: () =>
                        {
                            // Анимация завершена: снимаем блок
                            if (_deadPool.Value.Has(attackerEntity) && animPool.Has(attackerEntity))
                                animPool.Del(attackerEntity);

                            GameEventBus.Publish(new InputRestoredEvent());
                        }
                    );
                }
                else
                {
                    // Нет вью — мгновенный урон
                    if (!_hitPool.Value.Has(entity))
                    {
                        ref var hit = ref _hitPool.Value.Add(entity);
                        hit.TargetEntity = targetEntity;
                        hit.Amount       = attackValue;
                    }
                    _animPendingPool.Value.Del(entity);
                    GameEventBus.Publish(new InputRestoredEvent());
                }
            }
        }

        CreatureView GetView(int entity)
        {
            if (!_viewPool.Value.Has(entity)) return null;
            ref var vr = ref _viewPool.Value.Get(entity);
            if (vr.View == null) return null;
            return vr.View.GetComponent<CreatureView>();
        }
    }
}
