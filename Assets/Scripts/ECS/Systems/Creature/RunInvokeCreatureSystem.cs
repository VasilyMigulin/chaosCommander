using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// Существо вышло на борд (InvokeEvent):
    ///   • ВСЕГДА публикует CreatureInvokedEvent («выход на стол») — для реакций ДРУГИХ: реактивные ауры
    ///     и «на выходе»-способности (любой способ появления: розыгрыш/призыв).
    ///   • CardCastEvent («при разыгрывании» — своё battlecry) — только при настоящем КАСТЕ (!NotCast).
    ///     Призыв (NotCast=true) его НЕ публикует → нет каскада «при разыгрывании» (Работяга не зовёт цепочку).
    /// InvokeEvent снимает _delSystems.
    /// </summary>
    public sealed class RunInvokeCreatureSystem : IEcsRunSystem
    {
        readonly EcsFilterInject<Inc<InvokeEvent, CreatureTag>> _filter = default;
        readonly EcsPoolInject<InvokeEvent> _invokePool = default;


        public void Run(IEcsSystems systems)
        {
            foreach (var cardEntity in _filter.Value)
            {
                ref var invokeComp = ref _invokePool.Value.Get(cardEntity);

                GameEventBus.Publish(new CreatureInvokedEvent { CardEntity = cardEntity });

                if (!invokeComp.NotCast)
                    GameEventBus.Publish(new CardCastEvent { CardEntity = cardEntity });
            }
        }
    }
}
