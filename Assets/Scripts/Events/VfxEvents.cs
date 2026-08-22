using UnityEngine;

namespace Game.Core.Events
{
    // ─────────────────────────────────────────────────────────────────────────
    // КОСМЕТИЧЕСКИЕ VFX-события. Публикуются ECS-стороной (где есть BoardView/позиции),
    // потребляются VfxPresenter (Mono). Несут ГОТОВЫЕ мировые позиции + префаб → презентер
    // ничего не знает про ECS. На game-state НЕ влияют (идут на обоих клиентах, могут отличаться).
    //
    // МНОЖЕСТВО ЦЕЛЕЙ ОДНИМ СОБЫТИЕМ (не по одному на цель, как раньше): презентеру нужно видеть ВСЕ
    // цели разом, чтобы разложить их по Delivery (Split — параллельно, Chain — эстафета цель→цель).
    //
    // Enums движения лежат ЗДЕСЬ (не в Ecs.Components, где VfxSpec их авторит) — зависимость сборок
    // однонаправленная Ecs.Components→Events (см. VfxSpec.cs), обратно нельзя.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Доставка к НЕСКОЛЬКИМ целям (Projectile И Beam). Split — все ноги летят одновременно из
    /// каста (как раньше, дефолт — старые карты не меняют поведение). Chain — эстафета: каст→цель[0],
    /// затем цель[0]→цель[1] и так далее (визуально ОДИН объект/луч скачет по целям по очереди).</summary>
    public enum ProjectileDelivery { Split = 0, Chain = 1 }

    /// <summary>Форма траектории снаряда. Direct — прямая линия (как раньше). Ballistic — дуга (параболой,
    /// пик на середине пути высотой BallisticHeight) — «лобовой бросок» вместо выстрела по прямой.</summary>
    public enum ProjectileTrajectory { Direct = 0, Ballistic = 1 }

    /// <summary>Поведение ДВИЖЕНИЯ поверх траектории (не форма пути, а «дрожь» вдоль него). Linear — без
    /// отклонений (как раньше). Drift — плавный боковой снос (затухает к 0 в начале/конце — снаряд всё
    /// равно точно приходит в цель). Vortex — спиральное закручивание вокруг прямой цель-траектории.</summary>
    public enum ProjectileWobble { Linear = 0, Drift = 1, Vortex = 2 }

    /// <summary>Визуал луча (Beam). Straight — прямая линия 2 точки (как раньше). Lightning — ломаная из
    /// N сегментов со случайным джиттером поперёк луча (молния).</summary>
    public enum BeamStyle { Straight = 0, Lightning = 1 }

    /// <summary>Луч(и) каст→цели (Split) или цель→цель (Chain), опц. молния вместо прямой линии.</summary>
    public struct BeamVfxEvent : IGameEvent
    {
        public Vector3 From;
        public Vector3[] Targets;
        public GameObject Prefab;
        public GameObject HitPrefab;   // опц. вспышка на каждой цели
        public ProjectileDelivery Delivery;
        public BeamStyle Style;
        public int LightningSegments;
        public float LightningJitter;
    }

    /// <summary>Летящий снаряд(ы) каст→цели (Split) или эстафета цель→цель (Chain); на прилёте — HitPrefab
    /// на каждой посещённой цели. VfxArrivedEvent публикуется ОДИН раз — когда ВСЯ доставка завершена
    /// (последний Split-снаряд долетел / эстафета дошла до последней цели).</summary>
    public struct ProjectileVfxEvent : IGameEvent
    {
        public Vector3 From;
        public Vector3[] Targets;
        public GameObject Prefab;
        public GameObject HitPrefab;
        public float Speed;                    // units/сек
        public float Scale;                    // множитель масштаба инстанса (1 = как в префабе)
        public ProjectileDelivery Delivery;
        public ProjectileTrajectory Trajectory;
        public float BallisticHeight;
        public ProjectileWobble Wobble;
        public float WobbleAmount;
        public float WobbleFrequency;
        public int Token;              // ЛОКАЛЬНЫЙ id (ability-сущность) для VfxArrivedEvent; через сеть НЕ идёт
    }

    /// <summary>Снаряд долетел — резолв применяет отложенные эффекты этой способности. Token локален клиенту.</summary>
    public struct VfxArrivedEvent : IGameEvent
    {
        public int Token;
    }

    /// <summary>Эффект по области клеток. Merge=true → один эффект на общий Bounds; false → по клетке.</summary>
    public struct AreaVfxEvent : IGameEvent
    {
        public Vector3[] CellCenters;  // центры затронутых клеток
        public float CellSize;         // размер клетки (для масштаба/Bounds)
        public GameObject Prefab;
        public GameObject HitPrefab;   // опц. вспышка по каждой клетке
        public bool Merge;

        /// <summary>ЗОНА способности (Own/Enemy/All половина стола), если способность Field-режима —
        /// вместо баундов по фактическим CellCenters (те схлопываются в точку, если задета всего 1 цель,
        /// а Field-AOE концептуально бьёт по ВСЕЙ стороне, не по конкретным существам на ней). null →
        /// как раньше, баунды считаются по CellCenters (Target-способность/точка).</summary>
        public Bounds? ZoneBounds;
    }

    /// <summary>Разовая вспышка попадания в точке.</summary>
    public struct HitVfxEvent : IGameEvent
    {
        public Vector3 At;
        public GameObject Prefab;
        public float Scale;            // 0/1 = как есть
    }
}
