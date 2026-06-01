using Game.Core.Service;

namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Цветовой фильтр целей для способности.
    ///   • Required != None: кандидат должен иметь хотя бы один (AnyRequired=true)
    ///     или ВСЕ (AnyRequired=false) указанные цвета.
    ///   • Forbidden != None: кандидат не должен иметь ни одного из указанных цветов.
    /// Применяется RunResolveAbilityEffectSystem ко всем кандидатам-существам.
    /// </summary>
    public struct ColorRequirementComponent
    {
        public EnumService.Element Required;
        public EnumService.Element Forbidden;
        public bool AnyRequired;
    }
}
