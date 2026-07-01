namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Способность «в полёте»: снаряд (VfxKind.Projectile) запущен, но эффекты ЕЩЁ НЕ применены — ждём
    /// прилёта (VfxArrivedEvent от VfxPresenter). Пока висит на любой сущности — резолв и реплей-очередь
    /// не берут следующее действие (гейт, как AttackAnimPendingTag). AbilityQueuedState сохраняется до
    /// прилёта (там цели + канал генерации для синка). Deadline — анти-софтлок: если прилёт не пришёл
    /// (нет VfxPresenter/префаба) к этому времени, резолв применяет эффекты принудительно.
    /// </summary>
    public struct AbilityCastPendingComponent
    {
        public float Deadline;   // UnityEngine.Time.time, после которого форсим резолв
    }
}
