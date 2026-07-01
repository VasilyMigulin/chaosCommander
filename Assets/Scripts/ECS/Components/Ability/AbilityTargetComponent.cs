using Game.Core.Shared.Interface;

namespace Game.Core.Ecs.Components
{
    // === struct (Component) ===
    /// <summary>
    /// Таргетинг Target-способности (AbilityToTarget). Selection — Random/Selected,
    /// Count — сколько целей, Filters — какие цели валидны (враг/существо/цвет…).
    /// Заполняется в Ability.Init подтипом. У NonTarget-способности этого компонента нет.
    /// </summary>
    public struct AbilityTargetComponent
    {
        public TargetSelection Selection;
        public int Count;              // сколько целей ВЫБРАТЬ
        public int OfferCount;         // сколько ПОКАЗАТЬ в окне для Selected из не-Board зон (discover; 0 = все валидные). Board/Random игнорируют.
        public TargetZone Zone;        // откуда брать кандидатов (Board по умолчанию)
        public ITargetFilter[] Filters;
    }
}
