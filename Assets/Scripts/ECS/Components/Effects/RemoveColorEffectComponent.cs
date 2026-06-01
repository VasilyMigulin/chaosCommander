using Game.Core.Service;

namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Снимает цвета (флаги) с TargetEntity. Используется «Отлучение».
    /// </summary>
    public struct RemoveColorEffectComponent
    {
        public EnumService.Element Colors;
    }
}
