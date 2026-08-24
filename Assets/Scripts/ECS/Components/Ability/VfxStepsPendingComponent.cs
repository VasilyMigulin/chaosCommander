namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Состояние ЗАПУЩЕННОГО таймлайна VFX-шагов (AbilityVfxStepsComponent) для одной способности —
    /// аналог AbilityCastPendingComponent, но для НЕСКОЛЬКИХ параллельных/растянутых во времени шагов
    /// вместо одного снаряда. Launched[i] — шаг i уже отправлен (index-aligned со Steps списка на той же
    /// сущности). PendingArrivals — сколько уже ЗАПУЩЕННЫХ Projectile-шагов ещё не прислали
    /// VfxArrivedEvent. «Всё долетело» = все Launched[i]==true И PendingArrivals==0 — тогда
    /// RunResolveAbilityQueueSystem снимает этот компонент и применяет эффекты способности (как
    /// LandAndResolve для одиночного снаряда).
    /// </summary>
    public struct VfxStepsPendingComponent
    {
        public float ResolveStartTime;
        public bool[] Launched;
        public int PendingArrivals;
    }
}
