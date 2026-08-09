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

        /// <summary>Включать КОМАНДИРА в выборку из НЕ-Board зон (в Board он и так входит). По умолчанию
        /// false — командир неуязвим к дискарду/миллу/краже. true — для БЛАГОТВОРНЫХ способностей
        /// («Прямо пойдешь»: свои существа получают +1/+1/+1 ГДЕ БЫ НИ БЫЛИ — командир в руке тоже).</summary>
        public bool IncludeCommanderInZones;
    }
}
