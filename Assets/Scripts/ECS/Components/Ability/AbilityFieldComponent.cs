using Game.Core.Shared.Interface;

namespace Game.Core.Ecs.Components
{
    // === struct (Component) ===
    /// <summary>
    /// Таргетинг Field-способности (AbilityToField). Area — по какой части поля (всё/своя/вражеская),
    /// Filters — на кого воздействуем внутри области (враги/существа/цвет…). Применяется ко ВСЕМ
    /// подходящим. Заполняется в Ability.Init подтипом.
    /// </summary>
    public struct AbilityFieldComponent
    {
        public FieldArea Area;
        public TargetZone Zone;        // откуда брать кандидатов (Board по умолчанию)
        public ITargetFilter[] Filters;
    }
}
