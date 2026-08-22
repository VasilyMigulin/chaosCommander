namespace Game.Core.Ecs.Components
{
    // === struct (Component) ===
    /// <summary>
    /// Аватар выбран как атакующий (клик по своему аватару) — ждём клика по цели в row0 своей стороны.
    /// Аналог SelectTag для существ, но существо и аватар не смешиваются в одном фильтре (у аватара нет
    /// BoardPositionComponent/SpeedComponent, на которые опирается ветка SelectTag в RunSelectCellSystem).
    /// </summary>
    public struct AvatarSelectTag { }
}
