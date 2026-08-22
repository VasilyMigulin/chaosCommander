using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Mono;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Обрабатывает AttackRequestEvent на АВАТАРЕ (entity игрока) — атака row0 своей стороны (см.
    /// RunSelectCellSystem/RunAiTurnSystem). Параллель AttackSystem, но без CreatureTag/BoardTag/
    /// SpeedComponent (их у игрока нет) — лимит атак за ход считает свой AttacksUsedComponent на
    /// entity игрока (нет расхода Speed, аватар не двигается).
    /// </summary>
    public sealed class AvatarAttackSystem : IEcsRunSystem
    {
        readonly EcsFilterInject<
            Inc<AttackRequestEvent, PlayerComponent>,
            Exc<AttackAnimPendingTag>> _filter = default;

        readonly EcsPoolInject<AttackRequestEvent> _attackPool = default;
        readonly EcsPoolInject<AttackComponent> _atkValuePool = default;
        readonly EcsPoolInject<AttacksUsedComponent> _attacksUsedPool = default;
        readonly EcsPoolInject<AttackAnimPendingTag> _animPendingPool = default;
        readonly EcsPoolInject<AttackHitEvent> _hitPool = default;
        readonly EcsPoolInject<AvatarViewComponent> _avatarViewPool = default;
        readonly EcsPoolInject<DeadTag> _deadPool = default;

        const int MaxAttacksPerTurn = 1;   // как у существ (RunSelectCellSystem) — задел под будущие бонусы

        public void Run(IEcsSystems systems)
        {
            foreach (var entity in _filter.Value)
            {
                ref var req = ref _attackPool.Value.Get(entity);
                int targetEntity = req.TargetEntity;

                int used = _attacksUsedPool.Value.Has(entity) ? _attacksUsedPool.Value.Get(entity).Value : 0;
                if (used >= MaxAttacksPerTurn)
                {
                    _attackPool.Value.Del(entity);
                    continue;
                }

                int attackValue = _atkValuePool.Value.Has(entity) ? _atkValuePool.Value.Get(entity).Value : 0;
                if (attackValue <= 0)
                {
                    _attackPool.Value.Del(entity);
                    continue;
                }

                if (!_attacksUsedPool.Value.Has(entity)) _attacksUsedPool.Value.Add(entity);
                _attacksUsedPool.Value.Get(entity).Value = used + 1;
                _attackPool.Value.Del(entity);

                // Блокируем ввод/очередь на время анимации — как AttackSystem у существ.
                _animPendingPool.Value.Add(entity);
                GameEventBus.Publish(new InputBlockedEvent());

                int attackerEntity = entity;
                var hitPool  = _hitPool.Value;
                var animPool = _animPendingPool.Value;

                GameEventBus.Publish(new CreatureAttackedEvent
                {
                    AttackerEntity = attackerEntity,
                    DefenderEntity = targetEntity,
                });

                AvatarPlayerView view = GetView(entity);
                if (view != null)
                {
                    view.PlayAttack(
                        onHit: () =>
                        {
                            if (!_deadPool.Value.Has(attackerEntity) && !_deadPool.Value.Has(targetEntity))
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
                            if (animPool.Has(attackerEntity)) animPool.Del(attackerEntity);
                            GameEventBus.Publish(new InputRestoredEvent());
                        }
                    );
                }
                else
                {
                    // Нет вью (не должно случаться — у обоих игроков есть AvatarViewComponent, но на всякий
                    // случай тот же фолбэк, что и в AttackSystem при отсутствии CreatureView) — мгновенный урон.
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

        AvatarPlayerView GetView(int entity)
        {
            if (!_avatarViewPool.Value.Has(entity)) return null;
            ref var vc = ref _avatarViewPool.Value.Get(entity);
            if (vc.View == null) return null;
            return vc.View.GetComponent<AvatarPlayerView>();
        }
    }
}
