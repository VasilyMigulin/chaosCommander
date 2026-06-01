using Game.Core.Service;

namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Форма области эффекта способности. Вешается на entity способности при инициализации,
    /// если Shape != Single. Читается RunResolveAbilityEffectSystem: выбранный якорь
    /// разворачивается в набор клеток вокруг него.
    /// </summary>
    public struct TargetShapeComponent
    {
        public TargetShape Shape;
    }
}
