using Game.Core.Ecs.Components;
using Game.Core.Events;
using Leopotam.EcsLite;

namespace Game.Core.Ecs.Systems
{
    // === helper (static) ===
    /// <summary>
    /// «Пайплайн осел» — в мире НЕТ незавершённых действий: каста, резолва способности, снаряда в полёте,
    /// анимации каста/атаки, движения, отложенного OnCast, цепочки, анимации смерти, незакрытой раскопки.
    /// Раньше этот набор проверок был захардкожен в RunAiTurnSystem («не действуем, пока крутятся
    /// способности»), а RunActivateSystem/EndTurnRequestSystem держали СВОИ независимые копии — три списка
    /// разошлись (баг-класс 2026-08-22): каждый новый «ещё не завершено»-тег (ChainStateComponent,
    /// DeathAnimPendingTag, DiscoverRequestComponent) добавляли туда, где он оказывался нужен ПРЯМО СЕЙЧАС,
    /// и забывали остальные три места. Теперь ОДНО место — IsSettled — источник истины для всех четырёх
    /// (RunActivateSystem/EndTurnRequestSystem/RunAiTurnSystem/AutoCastSystem): новый тег добавляется
    /// ЗДЕСЬ один раз, и все вызывающие получают защиту автоматически.
    ///
    /// ЗАЧЕМ ВООБЩЕ: параллельные авто-касты (добрал 2 карты с «при взятии разыграй» — обе стартовали в
    /// одном кадре) гоняются друг с другом и с анимацией раздачи руки: UI не успевал освободить слот,
    /// модель теряла карту в списке руки. Последовательная очередь убирает саму возможность такой гонки.
    ///
    /// НЕ ВКЛЮЧАЙ СЮДА PendingSelectCellState (баг 2026-08-23, ход ИИ намертво вис после первого унификации):
    /// у RunAiTurnSystem это состояние — не «что-то ещё крутится, подожди», а «моё существо ждёт клетку,
    /// ИМЕННО СЕЙЧАС мой черёд действовать» (TryAct шаг 1 сам его резолвит). Если добавить сюда — AI
    /// заблокирует САМ СЕБЯ: PipelineBusy()=true, пока PendingSelectCellState не опустеет, а опустошает
    /// его только тот самый TryAct, который PipelineBusy() не даёт вызвать — замкнутый круг. Только у
    /// AutoCastSystem (единственный текущий вызывающий, где это уместно — сериализовать авто-касты) есть
    /// свой ОТДЕЛЬНЫЙ явный чек на этот тег, не через этот метод.
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
                && world.Filter<MovingTag>().End().GetEntitiesCount() == 0
                && world.Filter<AttackAnimPendingTag>().End().GetEntitiesCount() == 0
                && world.Filter<ChainStateComponent>().End().GetEntitiesCount() == 0
                && world.Filter<DeathAnimPendingTag>().End().GetEntitiesCount() == 0
                && world.Filter<DiscoverRequestComponent>().End().GetEntitiesCount() == 0
                && world.Filter<VfxStepsPendingComponent>().End().GetEntitiesCount() == 0;
        }

        /// <summary>Диагностика: какие именно компоненты сейчас держат пайплайн (для логов вида
        /// «передача хода застряла, не осели: ...» — см. EndTurnRequestSystem.ReportIfStuck). Пустая
        /// строка, если IsSettled(world) уже true.</summary>
        public static string DescribeBusy(EcsWorld world)
        {
            var sb = new System.Text.StringBuilder();
            Append(sb, "PresentationLock", PresentationLock.IsBusy ? 1 : 0);
            AppendFilter<RequestCardCastEvent>(sb, world, "RequestCardCastEvent");
            AppendFilter<CastEvent>(sb, world, "CastEvent");
            AppendFilter<AbilityCastEvent>(sb, world, "AbilityCastEvent");
            AppendFilter<AbilityTargetingState>(sb, world, "AbilityTargetingState");
            AppendFilter<AbilityQueuedState>(sb, world, "AbilityQueuedState");
            AppendFilter<AbilityCastPendingComponent>(sb, world, "AbilityCastPendingComponent");
            AppendFilter<AbilityAnimPendingComponent>(sb, world, "AbilityAnimPendingComponent");
            AppendFilter<PendingOnCastComponent>(sb, world, "PendingOnCastComponent");
            AppendFilter<MovingTag>(sb, world, "MovingTag");
            AppendFilter<AttackAnimPendingTag>(sb, world, "AttackAnimPendingTag");
            AppendFilter<ChainStateComponent>(sb, world, "ChainStateComponent");
            AppendFilter<DeathAnimPendingTag>(sb, world, "DeathAnimPendingTag");
            AppendFilter<DiscoverRequestComponent>(sb, world, "DiscoverRequestComponent");
            AppendFilter<VfxStepsPendingComponent>(sb, world, "VfxStepsPendingComponent");
            return sb.ToString();
        }

        static void AppendFilter<T>(System.Text.StringBuilder sb, EcsWorld world, string name) where T : struct
            => Append(sb, name, world.Filter<T>().End().GetEntitiesCount());

        static void Append(System.Text.StringBuilder sb, string name, int count)
        {
            if (count > 0) sb.Append(' ').Append(name).Append('=').Append(count);
        }
    }
}
