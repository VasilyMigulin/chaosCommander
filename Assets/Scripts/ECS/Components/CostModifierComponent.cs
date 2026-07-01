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
        /// <summary>Эффективная стоимость = базовая + модификатор владельца (не ниже 0).</summary>
        public static int Effective(EcsWorld world, int playerEntity, int baseCost)
        {
            var pool = world.GetPool<CostModifierComponent>();
            int mod = pool.Has(playerEntity) ? pool.Get(playerEntity).Amount : 0;
            int c = baseCost + mod;
            return c < 0 ? 0 : c;
        }
    }
}
