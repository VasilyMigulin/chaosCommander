using Game.Core.Ecs.Components;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;

namespace Game.Core.Model.Condition
{
    /// <summary>
    /// AbilityPlayRequirement — при разыгрывании карты цель выбирается автоматически случайным образом.
    ///
    /// При инициализации карты вешает RequiresRandomTargetTag и TargetRequirementComponent
    /// на entity карты. CastCardSystem не обрабатывает карту пока тег присутствует.
    /// RandomTargetSystem находит случайную цель, заполняет CastEvent.TargetEntity
    /// и снимает тег — каст продолжается.
    ///
    /// IsSatisfied проверяет наличие подходящих целей на доске, чтобы
    /// карта не подсвечивалась как доступная когда целей нет.
    /// </summary>
    public sealed class RequireRandomTargetPlayRequirement : AbilityPlayRequirement
    {
        public TargetRequirementType RequiredTarget;

        public RequireRandomTargetPlayRequirement(TargetRequirementType requiredTarget)
        {
            RequiredTarget = requiredTarget;
        }

        public override bool IsSatisfied(EcsWorld world, int cardEntity)
        {
            // Карта доступна только если на доске есть хотя бы одна подходящая цель
            var creaturePool  = world.GetPool<CreatureTag>();
            var boardPool     = world.GetPool<BoardTag>();
            var ownerPool     = world.GetPool<OwnerComponent>();
            var deadPool      = world.GetPool<DeadTag>();
            var cardOwnerPool = world.GetPool<OwnerComponent>();

            int ownerPlayerId = cardOwnerPool.Has(cardEntity)
                ? cardOwnerPool.Get(cardEntity).OwnerId
                : -1;

            var filter = world.Filter<CreatureTag>()
                              .Inc<BoardTag>()
                              .Inc<OwnerComponent>()
                              .Exc<DeadTag>()
                              .End();

            foreach (var ce in filter)
            {
                ref var owner = ref ownerPool.Get(ce);
                bool isEnemy = owner.OwnerId != ownerPlayerId;

                bool matches = RequiredTarget switch
                {
                    TargetRequirementType.RandomEnemy       => isEnemy,
                    TargetRequirementType.RandomAlly        => !isEnemy,
                    TargetRequirementType.RandomAnyCreature => true,
                    _                                       => false,
                };

                if (matches) return true;
            }

            return false;
        }

        public override IAbilityPlayRequirement Clone()
            => new RequireRandomTargetPlayRequirement(RequiredTarget);

        /// <summary>
        /// Вызывается при инициализации карты (Ability.AddPlayRequirement).
        /// Вешает тег и компонент типа цели на entity карты.
        /// </summary>
        public void ApplyToCard(EcsWorld world, int cardEntity)
        {
            if (!world.GetPool<RequiresRandomTargetTag>().Has(cardEntity))
                world.GetPool<RequiresRandomTargetTag>().Add(cardEntity);

            if (!world.GetPool<TargetRequirementComponent>().Has(cardEntity))
            {
                ref var comp = ref world.GetPool<TargetRequirementComponent>().Add(cardEntity);
                comp.RequiredTarget = RequiredTarget;
            }
        }
    }
}
