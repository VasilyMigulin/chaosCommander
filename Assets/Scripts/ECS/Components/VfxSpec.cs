using System;
using Game.Core.Events;
using UnityEngine;

namespace Game.Core.Ecs.Components
{
    /// <summary>Тип презентации каста. None — без VFX.</summary>
    public enum VfxKind { None = 0, Beam = 1, Projectile = 2, Area = 3 }

    /// <summary>
    /// КОСМЕТИЧЕСКАЯ спека VFX, авторится прямо на способности (Ability.Vfx). На game-state и синк НЕ влияет —
    /// только данные (префаб + тип). Инстанцирование строго во вью-слое (VfxPresenter). Держим в Ecs.Components,
    /// чтобы видеть из Ability (авторинг), Systems (публикация события) и Mono (рендер).
    ///
    /// ProjectileDelivery/ProjectileTrajectory/ProjectileWobble/BeamStyle лежат в Game.Core.Events (не здесь) —
    /// зависимость сборок однонаправленная Ecs.Components→Events, обратно нельзя; события (VfxEvents.cs) их
    /// тоже используют, поэтому единственное непротиворечивое место — Events.
    /// </summary>
    [Serializable]
    public class VfxSpec
    {
        public VfxKind Kind = VfxKind.None;

        [Tooltip("Beam: префаб с LineRenderer (каст→цель). Projectile: летящий объект (желательно с TrailRenderer). Area: эффект-партиклы.")]
        public GameObject Prefab;

        [Tooltip("Опц. вспышка попадания в цель/клетку (на прилёте снаряда/конце луча/по клеткам области).")]
        public GameObject HitPrefab;

        [Tooltip("Projectile: скорость полёта (units/сек).")]
        public float ProjectileSpeed = 12f;

        [Tooltip("Projectile: множитель масштаба инстанса (1 = как в префабе). Многие готовые VFX-паки " +
                 "(напр. Epic Toon FX) авторены под масштаб крупнее клетки доски — тут можно ужать под карту, " +
                 "не трогая сам (обычно переиспользуемый) префаб.")]
        public float ProjectileScale = 1f;

        [Tooltip("Area: true = ОДИН эффект, растянутый на общий Bounds клеток (сплошной прямоугольник); false = по эффекту НА КАЖДУЮ клетку (крест/произвольный набор).")]
        public bool MergeArea = false;

        [Header("Доставка к нескольким целям (Projectile/Beam)")]
        [Tooltip("Split — все летят сразу из каста параллельно (дефолт). Chain — эстафета: цель за целью, " +
                 "каждая следующая нога летит ОТ предыдущей цели, не от каста.")]
        public ProjectileDelivery Delivery = ProjectileDelivery.Split;

        [Header("Траектория (Projectile)")]
        public ProjectileTrajectory Trajectory = ProjectileTrajectory.Direct;
        [Tooltip("Пиковая высота дуги над прямой каст→цель, Trajectory=Ballistic.")]
        public float BallisticHeight = 3f;

        [Header("Поведение движения (Projectile)")]
        public ProjectileWobble Wobble = ProjectileWobble.Linear;
        [Tooltip("Амплитуда сноса/спирали (units), Wobble != Linear.")]
        public float WobbleAmount = 0.4f;
        [Tooltip("Сколько полных колебаний/оборотов за весь путь, Wobble != Linear.")]
        public float WobbleFrequency = 2f;

        [Header("Визуал луча (Beam)")]
        public BeamStyle BeamVisual = BeamStyle.Straight;
        [Tooltip("Число сегментов ломаной, BeamVisual=Lightning.")]
        public int LightningSegments = 10;
        [Tooltip("Амплитуда случайного джиттера поперёк луча (units), BeamVisual=Lightning.")]
        public float LightningJitter = 0.4f;

        [Tooltip("Кастер играет анимацию 'Cast' на СЕБЕ перед резолвом (напр. Чёрт — анимация и только потом " +
                 "огненная стрела). Эффекты применяются на Animation Event 'CastEvent', гейт снимается на " +
                 "'FinishEvent' (см. RunResolveAbilityQueueSystem/CreatureView.PlayAbilityCast). ОПЦИОНАЛЬНО " +
                 "(default false) — включай ТОЛЬКО когда в клипе кастера реально размечены эти два ивента, " +
                 "иначе резолв будет ждать anti-softlock таймаут (abilityCastMaxSeconds) впустую.")]
        public bool PlayCasterAnimation = false;
    }

    /// <summary>Спека VFX способности на ability-сущности (если задана). Ability.Init кладёт сюда Vfx,
    /// RunResolveAbilityQueueSystem читает и публикует косметические события. Идёт на обоих клиентах (резолв
    /// реплеится) → VFX виден у всех; на симуляцию не влияет.</summary>
    public struct AbilityVfxComponent
    {
        public VfxSpec Spec;
    }
}
