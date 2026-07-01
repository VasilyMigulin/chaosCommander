using Game.Core.Service;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;

namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Фильтр по принадлежности относительно владельца способности:
    /// Any / Self (свой) / Enemy (вражеский).
    /// </summary>
    [System.Serializable]
    public struct OwnershipFilter : IEntityFilter
    {
        public EnumService.Ownership Owner;

        public bool Matches(EcsWorld world, int candidateEntity, int ownerPlayerEntity)
        {
            if (Owner == EnumService.Ownership.Any) return true;

            var ownerPool = world.GetPool<OwnerComponent>();
            if (!ownerPool.Has(candidateEntity)) return false;

            var playerPool = world.GetPool<PlayerComponent>();
            if (!playerPool.Has(ownerPlayerEntity)) return false;

            int candidateOwnerId = ownerPool.Get(candidateEntity).OwnerId;
            int casterPlayerId   = playerPool.Get(ownerPlayerEntity).PlayerId;

            bool isSelf = candidateOwnerId == casterPlayerId;
            return Owner == EnumService.Ownership.Self ? isSelf : !isSelf;
        }
    }
}
