using Game.Core.Service;
using Game.Core.Shared.Interface;
using Game.Core.Ecs.Components;

namespace Game.Core.Model.Ability
{
    /// <summary>
    /// «Когда вы получаете урон (опц. только в свой ход) — нанесите столько же оппоненту».
    /// Используется «Вуду-будду» (OnlyOnOwnerTurn=true).
    ///
    /// Технически: подписывается на AuraEventType.OwnerPlayerDamaged. Когда вызвана,
    /// находит entity оппонента (другой PlayerComponent.PlayerId) и вешает на него
    /// TakeDamageEvent с amount = ctx.EventAmount, attacker = ctx.CharmEntity
    /// (для корректной атрибуции в логах).
    /// </summary>
    [System.Serializable]
    public class DamageReflectAuraReaction : AuraReaction
    {
        /// <summary>true → срабатывает только в свой ход (Вуду-будду).</summary>
        public bool OnlyOnOwnerTurn = true;

        /// <summary>Множитель амплитуды. 1 = равный возврат, 2 = двойной и т.д.</summary>
        public float DamageMultiplier = 1f;

        public override AuraEventType ListenTo => AuraEventType.OwnerPlayerDamaged;

        public override void OnEvent(AuraReactionContext ctx)
        {
            if (OnlyOnOwnerTurn && !ctx.OwnerIsActivePlayer) return;
            if (ctx.EventAmount <= 0) return;

            int reflectAmount = UnityEngine.Mathf.Max(1, UnityEngine.Mathf.RoundToInt(ctx.EventAmount * DamageMultiplier));

            int opponentEntity = FindOpponent(ctx.World, ctx.OwnerPlayerId);
            if (opponentEntity < 0) return;

            var takeDmgPool = ctx.World.GetPool<TakeDamageEvent>();
            if (takeDmgPool.Has(opponentEntity))
            {
                ref var existing = ref takeDmgPool.Get(opponentEntity);
                existing.Amount += reflectAmount;
            }
            else
            {
                ref var dmg = ref takeDmgPool.Add(opponentEntity);
                dmg.Amount = reflectAmount;
                dmg.Attacker = ctx.CharmEntity;
            }
        }

        static int FindOpponent(Leopotam.EcsLite.EcsWorld world, int ownerPlayerId)
        {
            var playerPool = world.GetPool<PlayerComponent>();
            foreach (var pe in world.Filter<PlayerComponent>().End())
            {
                if (playerPool.Get(pe).PlayerId != ownerPlayerId) return pe;
            }
            return -1;
        }
    }
}
