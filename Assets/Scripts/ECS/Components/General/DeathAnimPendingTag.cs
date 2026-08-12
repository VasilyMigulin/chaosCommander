namespace Game.Core.Ecs.Components
{
    // === struct (Component) ===
    /// <summary>
    /// Существо ещё доигрывает ВИЗУАЛЬНУЮ анимацию смерти (CreatureView.PlayDeath). Само логическое
    /// состояние (DeadTag/BoardTag) DieSystem.ProcessDeath снимает СИНХРОННО, ДО вызова PlayDeath —
    /// поэтому ни один из гейтов «мир осел» (RunChainSystem.WorldSettled/EndTurnRequestSystem.
    /// AbilitiesPending/RunActivateSystem/TurnTimerSystem/RunAiTurnSystem.PipelineBusy) её раньше не
    /// видел (баг 2026-08-11, PvE: игрок получал ход и добирал карту РАНЬШЕ, чем отыгралась анимация
    /// смерти вражеского существа и предсмертный эффект пришёл ему в руку — ИИ действует с фиксированным
    /// интервалом, который может быть короче анимации смерти). Вешает/снимает DieSystem.
    /// Deadline — анти-софтлок (доп. слой ПОВЕРХ собственного deathMaxSeconds-фолбэка CreatureView, на
    /// случай если Finish-колбэк потеряется, напр. вьюха уничтожена раньше срока).
    /// </summary>
    public struct DeathAnimPendingTag
    {
        public float Deadline;
    }
}
