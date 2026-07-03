using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Mono;
using UnityEngine;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// Гейт «призыв → OnCast» (#2). Существо, вышедшее на стол (RunInvokeCreatureSystem повесил
    /// PendingOnCastComponent), сначала спавнит вью и проигрывает анимацию призыва (SpawnCreatureViewSystem),
    /// и только ПОСЛЕ неё публикуется CardCastEvent (OnCast) + анимация каста. Так «при разыгрывании»
    /// (включая мгновенный SelfDestruct Всадников) срабатывает, когда существо уже видно и отыграло призыв,
    /// а не удаляется до появления вьюшки.
    ///
    /// Регистрируется в _generalSystems ПОСЛЕ SpawnCreatureViewSystem (чтобы вью уже существовала).
    /// Анти-софтлок: Deadline форсит публикацию, если вью/аниматора нет. Только активный клиент —
    /// пассив OnCast реплеит снапшотами (ActionAbilityData), PendingOnCast ему не вешают.
    /// </summary>
    public sealed class RunPendingOnCastSystem : IEcsRunSystem
    {
        readonly EcsFilterInject<Inc<PendingOnCastComponent, CreatureTag>> _filter = default;
        readonly EcsPoolInject<PendingOnCastComponent> _pool       = default;
        readonly EcsPoolInject<ViewSpawnedTag>         _spawnedPool = default;
        readonly EcsPoolInject<ViewRefComponent>       _viewPool    = default;
        readonly EcsPoolInject<DeadTag>                _deadPool    = default;
        readonly EcsPoolInject<BoardTag>               _boardPool   = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var entity in _filter.Value)
            {
                // Существо умерло/ушло с борда за время призыва → снять гейт без OnCast (не бьём battlecry
                // на трупе, не залипаем в ожидании). На ходу активного это почти невозможно, но защищаемся.
                if (_deadPool.Value.Has(entity) || !_boardPool.Value.Has(entity))
                {
                    _pool.Value.Del(entity);
                    continue;
                }

                ref var p = ref _pool.Value.Get(entity);

                bool ready;
                if (!_spawnedPool.Value.Has(entity))
                {
                    ready = false;                              // ждём спавна вью
                }
                else
                {
                    var view = GetView(entity);
                    ready = view == null || !view.IsSummoning;  // нет вью → не ждём; иначе ждём конца призыва
                }
                if (Time.time >= p.Deadline) ready = true;      // анти-софтлок

                if (!ready) continue;

                bool fireCast = p.FireCast;
                _pool.Value.Del(entity);

                if (fireCast)
                {
                    GameEventBus.Publish(new CardCastEvent { CardEntity = entity });
                    GetView(entity)?.PlayCast();
                }
            }
        }

        CreatureView GetView(int entity)
        {
            if (!_viewPool.Value.Has(entity)) return null;
            ref var vr = ref _viewPool.Value.Get(entity);
            return vr.View != null ? vr.View.GetComponent<CreatureView>() : null;
        }
    }
}
