using Game.Core.Shared.Interface;
using Leopotam.EcsLite;

namespace Game.Core.Model.Condition
{
    /// <summary>
    /// Базовый класс требования к полю боя, необходимого для розыгрыша карты.
    /// В отличие от AbilityCondition (которая проверяет условие активации способности
    /// уже разыгранной карты), AbilityPlayRequirement отвечает на вопрос:
    /// «Может ли карта вообще быть разыграна прямо сейчас?»
    /// Например: «на поле оппонента должно быть хотя бы одно существо»,
    /// «на поле должно быть существо с чёрным цветом» и т.д.
    /// </summary>
    public abstract class AbilityPlayRequirement : IAbilityPlayRequirement
    {
        public abstract bool IsSatisfied(EcsWorld world, int cardEntity);
        public abstract IAbilityPlayRequirement Clone();
    }
}
