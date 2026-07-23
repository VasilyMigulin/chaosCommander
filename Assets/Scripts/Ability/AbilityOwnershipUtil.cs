using Game.Core.Ecs.Components;
using Leopotam.EcsLite;

namespace Game.Core.Ability
{
    // === helper (static) ===
    /// <summary>
    /// ХС-семантика смены контроля: способности карты «служат» ТЕКУЩЕМУ владельцу. Триггеры «начало/конец
    /// хода», бенефициар эффектов (PlayerEntity) и свой/чужой в таргетинге капчурятся при Init — поэтому при
    /// смене контроля (TakeControlEffect, кража карты StealToHand/дискавер-воровство) и при откате
    /// (TempControlRevertSystem.RevertControl — зовёт IAbility.Rebind через интерфейс, без ссылки на эту
    /// сборку) способности ПЕРЕ-ПРИВЯЗЫВАЮТСЯ к новому игроку: тот же клон, та же ability-сущность,
    /// Dispose → очистка → Init (см. Ability.Rebind). Внутренний стейт инстансов переживает смену
    /// (OnMatchStartTrigger._fired, накопители условий).
    /// СИНК ДАРОМ: все вызывающие пути исполняются на обоих клиентах → пере-привязка зеркальна.
    /// </summary>
    public static class AbilityOwnershipUtil
    {
        /// <summary>Пере-привязать способности карты cardEntity к игроку newPlayerEntity (сущность игрока).</summary>
        public static void Rebind(EcsWorld world, int cardEntity, int newPlayerEntity)
        {
            if (newPlayerEntity < 0) return;
            var containerPool = world.GetPool<AbilityContainerComponent>();
            if (!containerPool.Has(cardEntity)) return;
            var entities = containerPool.Get(cardEntity).AbilityEntities;
            if (entities == null || entities.Length == 0) return;

            var refPool = world.GetPool<AbilityRefComponent>();
            for (int i = 0; i < entities.Length; i++)
            {
                int ae = entities[i];
                if (!refPool.Has(ae)) continue;
                refPool.Get(ae).Ability?.Rebind(world, ae, cardEntity, newPlayerEntity, i);
            }
        }
    }
}
