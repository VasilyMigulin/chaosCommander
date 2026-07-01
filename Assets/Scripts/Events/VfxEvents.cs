using UnityEngine;

namespace Game.Core.Events
{
    // ─────────────────────────────────────────────────────────────────────────
    // КОСМЕТИЧЕСКИЕ VFX-события. Публикуются ECS-стороной (где есть BoardView/позиции),
    // потребляются VfxPresenter (Mono). Несут ГОТОВЫЕ мировые позиции + префаб → презентер
    // ничего не знает про ECS. На game-state НЕ влияют (идут на обоих клиентах, могут отличаться).
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Мгновенный луч каст→цель (префаб с LineRenderer).</summary>
    public struct BeamVfxEvent : IGameEvent
    {
        public Vector3 From;
        public Vector3 To;
        public GameObject Prefab;
        public GameObject HitPrefab;   // опц. вспышка в точке To
    }

    /// <summary>Летящий снаряд каст→цель (объект + TrailRenderer), на прилёте — HitPrefab.</summary>
    public struct ProjectileVfxEvent : IGameEvent
    {
        public Vector3 From;
        public Vector3 To;
        public GameObject Prefab;
        public GameObject HitPrefab;
        public float Speed;            // units/сек
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
    }

    /// <summary>Разовая вспышка попадания в точке.</summary>
    public struct HitVfxEvent : IGameEvent
    {
        public Vector3 At;
        public GameObject Prefab;
        public float Scale;            // 0/1 = как есть
    }
}
