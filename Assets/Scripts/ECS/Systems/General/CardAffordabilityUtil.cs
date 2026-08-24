using Leopotam.EcsLite;
using Game.Core.Ecs.Components;

namespace Game.Core.Ecs.Systems
{
    // === helper === «хватает ли ресурсов на карту прямо сейчас» — вынесено из CardAffordabilitySystem
    // (подсветка руки), чтобы ту же логику мог переиспользовать TurnTimerSystem (шорткат таймера, если
    // у активного игрока вообще нет доступных действий) без дублирования условий костов.
    internal static class CardAffordabilityUtil
    {
        const int CharmLimit = 5;   // тот же лимит, что и pre-cost гейт в RunCastRouterSystem

        public static bool IsAffordable(EcsWorld world, int cardEntity)
        {
            var ownerPool = world.GetPool<OwnerComponent>();
            if (!ownerPool.Has(cardEntity)) return false;
            int ownerId = ownerPool.Get(cardEntity).OwnerId;
            int ownerEntity = FindPlayerEntity(world, ownerId);
            if (ownerEntity < 0) return false;

            if (!world.GetPool<ActiveState>().Has(ownerEntity)) return false;

            // Командир на кулдауне (после гибели) — недоступен к розыгрышу.
            if (world.GetPool<CommanderTag>().Has(cardEntity) && world.GetPool<CommanderCooldownComponent>().Has(cardEntity))
                return false;

            // Лимит чар (5) — та же проверка, что и в CardAffordabilitySystem.
            if (world.GetPool<CharmTag>().Has(cardEntity) && CharmCount(world, ownerId) >= CharmLimit)
                return false;

            // Доп. цена ПОВЕРХ обычной (RequiresAdditionalCostComponent, печатное свойство карты — не
            // AltCostComponent, тот временный маркер игрока для чужого следующего каста) — нечем платить
            // (сбросить/пожертвовать/смиллить нечего) → недоступна, как нехватка маны.
            var addCostPool = world.GetPool<RequiresAdditionalCostComponent>();
            if (addCostPool.Has(cardEntity)
                && !AltCostUtil.CanPay(world, addCostPool.Get(cardEntity).Kind, ownerId, cardEntity))
                return false;

            // Маркер альтернативной уплаты (Букмекер и семейство): играбельность = есть ли ЧЕМ платить.
            var altPool = world.GetPool<AltCostComponent>();
            if (altPool.Has(ownerEntity))
                return AltCostUtil.CanPay(world, altPool.Get(ownerEntity).Kind, ownerId, cardEntity);

            var goldCostPool = world.GetPool<GoldCostComponent>();
            var goldPool = world.GetPool<GoldComponent>();
            if (goldCostPool.Has(cardEntity) && goldPool.Has(ownerEntity))
                return goldPool.Get(ownerEntity).Current >= CostModifierUtil.Effective(world, ownerEntity, cardEntity, goldCostPool.Get(cardEntity).Cost);

            var manaCostPool = world.GetPool<ManaCostComponent>();
            var manaPool = world.GetPool<ManaComponent>();
            if (manaCostPool.Has(cardEntity) && manaPool.Has(ownerEntity))
                return manaPool.Get(ownerEntity).Current >= CostModifierUtil.Effective(world, ownerEntity, cardEntity, manaCostPool.Get(cardEntity).Cost);

            // Карта без ресурсного коста (напр. HealthCost — «суицид» разрешён всегда) — доступна.
            return true;
        }

        // Токены не считаются — см. тот же комментарий в RunCastRouterSystem.CharmCount.
        static int CharmCount(EcsWorld world, int ownerId)
        {
            var ownerPool = world.GetPool<OwnerComponent>();
            int n = 0;
            foreach (var e in world.Filter<CharmTag>().Inc<BoardTag>().Exc<TokenTag>().End())
                if (ownerPool.Has(e) && ownerPool.Get(e).OwnerId == ownerId) n++;
            return n;
        }

        static int FindPlayerEntity(EcsWorld world, int playerId)
        {
            var playerPool = world.GetPool<PlayerComponent>();
            foreach (var e in world.Filter<PlayerComponent>().End())
                if (playerPool.Get(e).PlayerId == playerId) return e;
            return -1;
        }
    }
}
