using Game.Core.Service;

namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Маска целей способности. Вешается на entity способности при инициализации.
    /// Читается системами резолва (RunResolveAbilityEffectSystem / RunResolveAbilityFieldSystem)
    /// и аурами (AuraRecalcSystem) для выбора целей эффекта.
    /// </summary>
    public struct TargetMaskComponent
    {
        public TargetMask Mask;
    }
}
