using Leopotam.EcsLite;

namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Модификатор стоимости розыгрыша карт игрока (напр. Гиперинфляция: «все карты на 1 дороже»).
    /// Висит на сущности ИГРОКА; читается при оплате (RunCastRouterSystem) и при расчёте доступности
    /// (CardAffordabilitySystem). Одноразовый перманентный эффект (до конца матча) — НЕ аура.
    /// </summary>
    public struct CostModifierComponent
    {
        public int Amount;
    }

    public static class CostModifierUtil
    {
        /// <summary>Эффективная стоимость = базовая + перманентный модификатор владельца + сумма активных
        /// ауро-модификаторов (AuraCostModifierComponent, с учётом цвета cardEntity — см. AuraCostModifiers.
        /// Sum), не ниже 0. cardEntity нужен ТОЛЬКО ради цвета — если у карты нет CardModelComponent
        /// (не должно случаться для реальной карты), ауро-модификаторы просто не фильтруют по цвету.</summary>
        public static int Effective(EcsWorld world, int playerEntity, int cardEntity, int baseCost)
        {
            var pool = world.GetPool<CostModifierComponent>();
            int mod = pool.Has(playerEntity) ? pool.Get(playerEntity).Amount : 0;

            var modelPool = world.GetPool<CardModelComponent>();
            var cardElement = modelPool.Has(cardEntity) ? modelPool.Get(cardEntity).Element : default;
            mod += AuraCostModifiers.Sum(world, playerEntity, cardElement);

            int c = baseCost + mod;
            return c < 0 ? 0 : c;
        }
    }
}
