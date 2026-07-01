using System;
using UnityEngine;

namespace Game.Core.Ecs.Components
{
    /// <summary>Тип презентации каста. None — без VFX.</summary>
    public enum VfxKind { None, Beam, Projectile, Area }

    /// <summary>
    /// КОСМЕТИЧЕСКАЯ спека VFX, авторится прямо на способности (Ability.Vfx). На game-state и синк НЕ влияет —
    /// только данные (префаб + тип). Инстанцирование строго во вью-слое (VfxPresenter). Держим в Ecs.Components,
    /// чтобы видеть из Ability (авторинг), Systems (публикация события) и Mono (рендер).
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

        [Tooltip("Area: true = ОДИН эффект, растянутый на общий Bounds клеток (сплошной прямоугольник); false = по эффекту НА КАЖДУЮ клетку (крест/произвольный набор).")]
        public bool MergeArea = false;
    }

    /// <summary>Спека VFX способности на ability-сущности (если задана). Ability.Init кладёт сюда Vfx,
    /// RunResolveAbilityQueueSystem читает и публикует косметические события. Идёт на обоих клиентах (резолв
    /// реплеится) → VFX виден у всех; на симуляцию не влияет.</summary>
    public struct AbilityVfxComponent
    {
        public VfxSpec Spec;
    }
}
