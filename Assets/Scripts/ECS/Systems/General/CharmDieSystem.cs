using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using UnityEngine;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// Уничтожение ЧАРЫ с DeadTag (истёк таймер / эффект): публикует CreatureDiedEvent (OnDie-способности
    /// чары + авто-ревёрт выданных ею аур), снимает с поля → кладбище (токен → лимбо), убирает визуал.
    /// DieSystem чары не трогает (он Inc<...,CreatureTag>). Работает на ОБОИХ клиентах: актив вешает DeadTag
    /// через CharmTimerTickSystem, пассив — через ReplayDeath (ActionDeathData) → оба уничтожают одинаково.
    /// </summary>
    public sealed class CharmDieSystem : IEcsRunSystem
    {
        readonly EcsFilterInject<Inc<DeadTag, BoardTag, CharmTag>> _filter = default;
        readonly EcsPoolInject<BoardTag> _boardPool = default;
        readonly EcsPoolInject<GraveTag> _gravePool = default;
        readonly EcsPoolInject<LimboTag> _limboPool = default;
        readonly EcsPoolInject<TokenTag> _tokenPool = default;
        readonly EcsPoolInject<BoardPositionComponent> _posPool = default;
        readonly EcsPoolInject<ViewRefComponent> _viewPool = default;
        readonly EcsPoolInject<CharmTimerComponent> _timerDbgPool = default;   // ВРЕМЕННО: диагностика ранней смерти

        public void Run(IEcsSystems systems)
        {
            foreach (var entity in _filter.Value)
            {
                // ВРЕМЕННО (диагностика «Дупликатор умирает раньше срока»): кто умер и с каким остатком таймера.
                UnityEngine.Debug.Log($"[CharmDie] entity={entity} frame={UnityEngine.Time.frameCount} " +
                    $"timer={(  _timerDbgPool.Value.Has(entity) ? _timerDbgPool.Value.Get(entity).TurnsRemaining.ToString() : "нет")}");

                // Шина: OnDie чары + ревёрт аур, выданных этой чарой (ApplyTrackedBuffEffect слушает смерть источника).
                GameEventBus.Publish(new CreatureDiedEvent { CardEntity = entity, KillerEntity = -1 });

                _boardPool.Value.Del(entity);
                if (_posPool.Value.Has(entity)) _posPool.Value.Del(entity);

                if (_viewPool.Value.Has(entity))
                {
                    ref var vr = ref _viewPool.Value.Get(entity);
                    if (vr.View != null) Object.Destroy(vr.View);
                    vr.View = null;
                }

                // Токен — в лимбо (не на кладбище), обычная чара — на кладбище.
                if (_tokenPool.Value.Has(entity)) _limboPool.Value.Add(entity);
                else                              _gravePool.Value.Add(entity);
            }
        }
    }
}
