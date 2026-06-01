using Game.Core.Ecs.Components;
using Game.Core.Service;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;

namespace Game.Core.Model.Condition
{
    /// <summary>
    /// Авто-добавляется Ability.Init для режимов Select / Random.
    /// Гейтит PlayableTag: карта недоступна, если на поле нет подходящей цели.
    /// (Для Auto не нужен — авто-таргетинг просто никого не найдёт и ничего не сделает.)
    /// </summary>
    public sealed class RequireValidTargetPlayRequirement : AbilityPlayRequirement
    {
        public readonly TargetMask Mask;

        public RequireValidTargetPlayRequirement(TargetMask mask) { Mask = mask; }

        public override bool IsSatisfied(EcsWorld world, int cardEntity)
        {
            // Игроки и Self всегда существуют — не блокируем
            if (Mask.Has(TargetMask.Self)
             || Mask.Has(TargetMask.AllyPlayer)
             || Mask.Has(TargetMask.EnemyPlayer))
                return true;

            bool wantAlly  = Mask.Has(TargetMask.AllyCreature);
            bool wantEnemy = Mask.Has(TargetMask.EnemyCreature);
            if (!wantAlly && !wantEnemy) return true; // нет фильтра по существам — не гейтим

            var ownerPool = world.GetPool<OwnerComponent>();
            int ownerPlayerId = ownerPool.Has(cardEntity) ? ownerPool.Get(cardEntity).OwnerId : -1;

            foreach (var ce in world.Filter<CreatureTag>()
                                    .Inc<BoardTag>()
                                    .Inc<OwnerComponent>()
                                    .Exc<DeadTag>()
                                    .End())
            {
                bool isAlly = ownerPool.Get(ce).OwnerId == ownerPlayerId;
                if (isAlly  && wantAlly)  return true;
                if (!isAlly && wantEnemy) return true;
            }

            return false;
        }

        public override IAbilityPlayRequirement Clone() => new RequireValidTargetPlayRequirement(Mask);
    }
}
