using Game.Core.Service;

namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Добавить цвета (флаги) к TargetEntity (карте/существу): для каждого установленного
    /// бита в Colors вешается соответствующий *Tag (RedTag/BlueTag/...).
    /// </summary>
    public struct AddColorEffectComponent
    {
        public EnumService.Element Colors;
    }
}
