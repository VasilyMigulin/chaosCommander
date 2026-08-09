using Game.Core.Ecs.Components;
using Game.Core.Events;
using Leopotam.EcsLite;

namespace Game.Core.Ecs.Systems
{
    // === helper (static) ===
    /// <summary>
    /// «Пайплайн осел» — в мире НЕТ незавершённых действий: каста, резолва способности, снаряда в полёте,
    /// анимации каста/атаки, движения, отложенного OnCast. Раньше этот набор проверок был захардкожен
    /// в RunAiTurnSystem («не действуем, пока крутятся способности»); вынесен, чтобы им пользовалась и
    /// очередь авто-розыгрышей (AutoCastSystem) — последовательный, а не параллельный форс-каст.
    ///
    /// ЗАЧЕМ: параллельные авто-касты (добрал 2 карты с «при взятии разыграй» — обе стартовали в одном
    /// кадре) гоняются друг с другом и с анимацией раздачи руки: UI не успевал освободить слот, модель
    /// теряла карту в списке руки. Последовательная очередь убирает саму возможность такой гонки.
    /// </summary>
    public static class PipelineGate
    {
        public static bool IsSettled(EcsWorld world)
        {
            // Анимации ПРЕЗЕНТАЦИИ (раздача руки, розыгрыш карты в UI) — такая же незавершённая работа,
            // как полёт снаряда или анимация каста. Замок берёт сам UI и сам отпускает, поэтому там, где
            // UI нет (пассив, ход ИИ, headless), это условие всегда истинно и никого не задерживает.
            return !PresentationLock.IsBusy
                && world.Filter<RequestCardCastEvent>().End().GetEntitiesCount() == 0
                && world.Filter<CastEvent>().End().GetEntitiesCount() == 0
                && world.Filter<AbilityCastEvent>().End().GetEntitiesCount() == 0
                && world.Filter<AbilityTargetingState>().End().GetEntitiesCount() == 0
                && world.Filter<AbilityQueuedState>().End().GetEntitiesCount() == 0
                && world.Filter<AbilityCastPendingComponent>().End().GetEntitiesCount() == 0
                && world.Filter<AbilityAnimPendingComponent>().End().GetEntitiesCount() == 0
                && world.Filter<PendingOnCastComponent>().End().GetEntitiesCount() == 0
                && world.Filter<PendingSelectCellState>().End().GetEntitiesCount() == 0
                && world.Filter<MovingTag>().End().GetEntitiesCount() == 0
                && world.Filter<AttackAnimPendingTag>().End().GetEntitiesCount() == 0;
        }
    }
}
