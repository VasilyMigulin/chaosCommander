using Game.Core.Ecs.Components;
using Game.Core.Events;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Вешает кулдаун командиру на ЛЮБОЙ возврат в руку (переход «не в руке → в руке»), а не только
    /// на смерть. Ловит переход по HandTag, поэтому покрывает и смерть (DieSystem), и баунс/«вернуть
    /// в руку» (RunLeaveBoardSystem) — источникам возврата ничего знать про кулдаун не нужно.
    ///
    /// Первое наблюдение командира инициализирует трекер без срабатывания — стартовая рука (командир
    /// раздаётся в руку на старте) кулдаун НЕ даёт. Детерминированно: система в EcsRunHandler крутится
    /// на обоих клиентах, переход HandTag синкается штатными системами.
    /// </summary>
    public sealed class RunCommanderCooldownSystem : IEcsRunSystem
    {
        const int ReturnCooldown = 1;

        readonly EcsFilterInject<Inc<CommanderTag>> _commanders = default;
        readonly EcsPoolInject<HandTag> _handTag = default;
        readonly EcsPoolInject<CommanderCooldownComponent> _cooldown = default;
        readonly EcsPoolInject<CommanderHandTracker> _tracker = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var entity in _commanders.Value)
            {
                bool inHand = _handTag.Value.Has(entity);

                // Первое наблюдение — только инициализируем трекер (без кулдауна на стартовую руку).
                if (!_tracker.Value.Has(entity))
                {
                    ref var init = ref _tracker.Value.Add(entity);
                    init.WasInHand = inHand;
                    continue;
                }

                ref var t = ref _tracker.Value.Get(entity);

                // Переход «не в руке (поле/лимбо) → рука» = возврат командира → кулдаун.
                if (inHand && !t.WasInHand)
                {
                    if (!_cooldown.Value.Has(entity)) _cooldown.Value.Add(entity);
                    _cooldown.Value.Get(entity).TurnsRemaining = ReturnCooldown;

                    GameEventBus.Publish(new CommanderOnCooldownUIEvent
                    {
                        CardEntity    = entity,
                        CooldownTurns = ReturnCooldown,
                    });
                }

                t.WasInHand = inHand;
            }
        }
    }
}
