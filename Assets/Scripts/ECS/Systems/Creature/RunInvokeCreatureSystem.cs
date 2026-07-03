using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using UnityEngine;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// Существо вышло на борд (InvokeEvent):
    ///   • ВСЕГДА публикует CreatureInvokedEvent («выход на стол») — для реакций ДРУГИХ: реактивные ауры
    ///     и «на выходе»-способности + синк размещения (ActionCastData). Остаётся мгновенным.
    ///   • «При разыгрывании» (CardCastEvent) НЕ публикуется сразу: вешаем PendingOnCastComponent (гейт #2).
    ///     RunPendingOnCastSystem опубликует CardCastEvent после анимации призыва (существо уже на столе).
    ///     FireCast = настоящий КАСТ (!NotCast); тихий призыв (NotCast) анимируется, но battlecry не фейрит.
    /// InvokeEvent снимает _delSystems.
    /// </summary>
    public sealed class RunInvokeCreatureSystem : IEcsRunSystem
    {
        readonly EcsFilterInject<Inc<InvokeEvent, CreatureTag>> _filter = default;
        readonly EcsPoolInject<InvokeEvent> _invokePool = default;
        readonly EcsPoolInject<PendingOnCastComponent> _pendingPool = default;

        const float OnCastDeadline = 3f;   // анти-софтлок: форс OnCast, если призыв не «дозреет»

        public void Run(IEcsSystems systems)
        {
            foreach (var cardEntity in _filter.Value)
            {
                ref var invokeComp = ref _invokePool.Value.Get(cardEntity);

                GameEventBus.Publish(new CreatureInvokedEvent { CardEntity = cardEntity });

                if (!_pendingPool.Value.Has(cardEntity)) _pendingPool.Value.Add(cardEntity);
                ref var p = ref _pendingPool.Value.Get(cardEntity);
                p.FireCast = !invokeComp.NotCast;
                p.Deadline = Time.time + OnCastDeadline;
            }
        }
    }
}
