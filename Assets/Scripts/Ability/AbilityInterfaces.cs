using System;

namespace Game.Core.Ability
{
    // === class (OOP), все реализации ===
    // Это НЕ ECS-компоненты: они не наследуют IComponent/AddComponent.
    // Это живые объекты-стратегии, которые клонируются на карту и знают свой контекст.

    // ITrigger живёт в Game.Core.Shared.Interface (контейнер — AbilityTriggerContainerComponent на сущности).
    // IRule живёт в Game.Core.Shared.Interface (не контейнер; список — в AbilityRuleContainerComponent).

    // Доставка (projectile/instant) — косметика/контент будущего (визуал снаряда + view-система).
    // На game-state и синк не влияет (резолв применяет эффекты мгновенно), поэтому в пайплайне не нужна.

    /// <summary>Условие "готов ли эффект" (Ready). Event-driven: Changed дёргается при смене IsReady.</summary>
    public interface ICondition
    {
        bool IsReady { get; }
        event Action Changed;
        void Init(AbilityContext ctx);
        void Dispose();
    }

    // IEffect живёт в Game.Core.Shared.Interface (контейнер эффектов — AbilityEffectContainerComponent на сущности).
}
