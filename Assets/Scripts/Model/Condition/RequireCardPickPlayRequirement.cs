using Game.Core.Ecs.Components;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;

namespace Game.Core.Model.Condition
{
    /// <summary>
    /// Требование разыгрывания карты: перед кастом игрок должен выбрать одну карту
    /// из предложенного пула (механика раскопки).
    ///
    /// При инициализации карты (Ability.AddPlayRequirement) вызывает ApplyToCard,
    /// которая навешивает RequiresCardPickTag и CardPickRequirementComponent
    /// на entity карты. CastCardSystem не запустится пока тег присутствует.
    ///
    /// CardPickSelectionSystem собирает пул согласно Source, публикует
    /// CardPickOfferedEvent, ждёт CardPickChosenEvent от UI, затем снимает тег
    /// и записывает CardPickResultComponent — чтобы эффекты способности знали
    /// что именно выбрал игрок.
    /// </summary>
    public sealed class RequireCardPickPlayRequirement : AbilityPlayRequirement
    {
        public readonly CardPickSourceType Source;

        /// <summary>Сколько карт предложить игроку (обычно 3).</summary>
        public readonly int OfferCount;

        /// <summary>
        /// Только для Source == UniquePool.
        /// ModelId карт из которых случайно отбираются OfferCount штук.
        /// </summary>
        public readonly int[] UniquePoolModelIds;

        public RequireCardPickPlayRequirement(
            CardPickSourceType source,
            int offerCount = 3,
            int[] uniquePoolModelIds = null)
        {
            Source             = source;
            OfferCount         = offerCount;
            UniquePoolModelIds = uniquePoolModelIds;
        }

        public override bool IsSatisfied(EcsWorld world, int cardEntity)
        {
            // Не блокируем видимость карты в руке — только сам каст.
            return true;
        }

        public override IAbilityPlayRequirement Clone()
            => new RequireCardPickPlayRequirement(Source, OfferCount, UniquePoolModelIds);

        /// <summary>
        /// Вызывается при инициализации карты (Ability.AddPlayRequirement).
        /// Вешает тег и компонент требования на entity карты.
        /// </summary>
        public void ApplyToCard(EcsWorld world, int cardEntity)
        {
            if (!world.GetPool<RequiresCardPickTag>().Has(cardEntity))
                world.GetPool<RequiresCardPickTag>().Add(cardEntity);

            if (!world.GetPool<CardPickRequirementComponent>().Has(cardEntity))
            {
                ref var comp = ref world.GetPool<CardPickRequirementComponent>().Add(cardEntity);
                comp.Source             = Source;
                comp.OfferCount         = OfferCount;
                comp.UniquePoolModelIds = UniquePoolModelIds;
            }
        }
    }
}
