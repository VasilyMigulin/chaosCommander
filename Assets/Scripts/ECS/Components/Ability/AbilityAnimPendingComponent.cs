namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Способность играет анимацию КАСТА на кастере (CreatureView.PlayAbilityCast, VfxSpec.PlayCasterAnimation).
    /// Эффекты применяются на Animation Event "CastEvent" (CastApplied=false→true), гейт снимается на
    /// "FinishEvent". Пока висит на ЛЮБОЙ сущности — очередь способностей и таймер хода не продвигаются
    /// (тот же принцип, что и у AbilityCastPendingComponent для снарядов; оба гейта независимы и могут
    /// существовать одновременно — напр. Cast-анимация кастера + отдельно летящий снаряд).
    /// Deadline — анти-софтлок: если клип не размечен обоими ивентами, форсим оба по таймауту.
    /// </summary>
    public struct AbilityAnimPendingComponent
    {
        public float Deadline;
        public bool CastApplied;   // true после применения эффектов (CastEvent или форс по таймауту) — не дублировать
    }
}
