using System;
using System.Collections.Generic;

namespace Game.Core.Ecs.Components
{
    /// <summary>Откуда/куда летит VFX-шаг. Caster — сущность карты-источника способности. Owner — сущность
    /// ИГРОКА-владельца (полезно для «выстрел от аватара», не от конкретного существа). Target — цель(и)
    /// резолва способности (Destination берёт ВСЕ цели разом, как сегодняшний From→Targets; Source, если
    /// = Target, берёт первую — большинство карт используют Target только как Destination).</summary>
    public enum VfxEndpoint { Caster = 0, Owner = 1, Target = 2 }

    /// <summary>
    /// Один шаг VFX-таймлайна способности (Ability.VfxSteps) — конструктор поверх обычного VfxSpec:
    /// то же самое (Kind/Prefab/Delivery/Trajectory/...) плюс откуда-куда летит и когда стартует.
    /// StartDelay — АБСОЛЮТНОЕ время от начала резолва способности (не «после предыдущего шага») — это
    /// одно и то же поле даёт и очередь (разные StartDelay), и параллель (одинаковый StartDelay), и любую
    /// смесь. См. VfxStepsPendingComponent/RunResolveAbilityQueueSystem.LaunchVfxSteps.
    /// Наследует VfxSpec (не дублирует поля) — совместим с VfxEmitUtil.EmitInstantVfx/LaunchProjectile
    /// как есть (те принимают VfxSpec).
    /// </summary>
    [Serializable]
    public sealed class VfxStep : VfxSpec
    {
        public VfxEndpoint Source = VfxEndpoint.Caster;
        public VfxEndpoint Destination = VfxEndpoint.Target;

        [UnityEngine.Tooltip("Секунды от НАЧАЛА резолва способности (не от предыдущего шага). Несколько " +
                              "шагов с одинаковым значением — параллельно; с разными — очередь/россыпь.")]
        public float StartDelay = 0f;
    }

    /// <summary>Список VFX-шагов способности (Ability.VfxSteps, если непусто — ПРИОРИТЕТНЕЕ одиночного
    /// Ability.Vfx/AbilityVfxComponent, см. Ability.Init). Сам список — печатные данные (шаблон);
    /// рантайм-состояние запуска — VfxStepsPendingComponent.</summary>
    public struct AbilityVfxStepsComponent
    {
        public List<VfxStep> Steps;
    }
}
