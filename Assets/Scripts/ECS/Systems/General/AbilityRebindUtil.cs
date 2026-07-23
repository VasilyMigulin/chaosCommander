using Game.Core.Ecs.Components;
using Leopotam.EcsLite;

namespace Game.Core.Ecs.Systems
{
    // === helper (static) ===
    /// <summary>
    /// ХС-пере-привязка способностей карты новому игроку ИЗ ECS-СИСТЕМ — через IAbility.Rebind
    /// (Shared.Interface): сборка Systems не ссылается на сборку поведения Game.Core.Ability (та же
    /// граница, что у резолва эффектов). Эффекты из сборки Ability используют свой AbilityOwnershipUtil.
    /// ПРАВИЛО: любой код, меняющий OwnerComponent КАРТЫ (кража/откат/возврат командира), обязан звать
    /// пере-привязку — иначе триггеры/бенефициар останутся верны старому владельцу (тихий баг).
    /// </summary>
    public static class AbilityRebindUtil
    {
        public static void Rebind(EcsWorld world, int cardEntity, int newPlayerEntity)
        {
            if (newPlayerEntity < 0) return;
            var containerPool = world.GetPool<AbilityContainerComponent>();
            if (!containerPool.Has(cardEntity)) return;
            var abilities = containerPool.Get(cardEntity).AbilityEntities;
            if (abilities == null) return;

            var refPool = world.GetPool<AbilityRefComponent>();
            for (int i = 0; i < abilities.Length; i++)
            {
                int ae = abilities[i];
                if (!refPool.Has(ae)) continue;
                refPool.Get(ae).Ability?.Rebind(world, ae, cardEntity, newPlayerEntity, i);
            }
        }

        public static void RebindToPlayerId(EcsWorld world, int cardEntity, int playerId)
            => Rebind(world, cardEntity, FindPlayerEntity(world, playerId));

        public static int FindPlayerEntity(EcsWorld world, int playerId)
        {
            var pp = world.GetPool<PlayerComponent>();
            foreach (var pe in world.Filter<PlayerComponent>().End())
                if (pp.Get(pe).PlayerId == playerId) return pe;
            return -1;
        }
    }
}
