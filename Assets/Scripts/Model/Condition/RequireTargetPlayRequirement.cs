using Game.Core.Ecs.Components;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;

namespace Game.Core.Model.Condition
{
    /// <summary>
    /// AbilityPlayRequirement — требует явного выбора цели перед кастом карты.
    ///
    /// При инициализации карты вешает RequiresTargetSelectionTag на entity карты.
    /// TargetSelectionSystem перехватывает управление, подсвечивает валидные цели
    /// и снимает тег как только игрок кликает по одной из них.
    /// CastCardSystem не обработает CastEvent пока тег присутствует.
    ///
    /// IsSatisfied всегда возвращает true — этот requirement не блокирует PlayableTag,
    /// он лишь сигнализирует что нужен интерактивный выбор цели.
    /// </summary>
    public sealed class RequireTargetPlayRequirement : AbilityPlayRequirement
    {
        public TargetRequirementType RequiredTarget;

        public RequireTargetPlayRequirement(TargetRequirementType requiredTarget)
        {
            RequiredTarget = requiredTarget;
        }

        public override bool IsSatisfied(EcsWorld world, int cardEntity)
        {
            // Не блокируем видимость карты в руке — только требуем выбор цели перед кастом
            return true;
        }

        public override IAbilityPlayRequirement Clone()
            => new RequireTargetPlayRequirement(RequiredTarget);

        /// <summary>
        /// Вызывается при инициализации карты (Ability.AddPlayRequirement).
        /// Вешает тег и компонент типа цели на entity карты.
        /// </summary>
        public void ApplyToCard(EcsWorld world, int cardEntity)
        {
            if (!world.GetPool<RequiresTargetSelectionTag>().Has(cardEntity))
                world.GetPool<RequiresTargetSelectionTag>().Add(cardEntity);

            if (!world.GetPool<TargetRequirementComponent>().Has(cardEntity))
            {
                ref var comp = ref world.GetPool<TargetRequirementComponent>().Add(cardEntity);
                comp.RequiredTarget = RequiredTarget;
            }
        }
    }
}
