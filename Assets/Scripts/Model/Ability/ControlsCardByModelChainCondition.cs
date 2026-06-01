using Leopotam.EcsLite;
using Game.Core.Ecs.Components;

namespace Game.Core.Model.Ability
{
    /// <summary>«Если владелец контролирует карту с указанным ModelId на доске».</summary>
    [System.Serializable]
    public class ControlsCardByModelChainCondition : ChainCondition
    {
        public int ModelId;

        public override bool Evaluate(EcsWorld world, int abilityEntity, int sourceCard, int ownerPlayerId, int ownerPlayerEntity)
        {
            var modelPool = world.GetPool<CardModelComponent>();
            var ownerPool = world.GetPool<OwnerComponent>();
            foreach (var ce in world.Filter<BoardTag>().Inc<OwnerComponent>().Inc<CardModelComponent>().End())
            {
                if (ownerPool.Get(ce).OwnerId != ownerPlayerId) continue;
                if (modelPool.Get(ce).ModelId == ModelId) return true;
            }
            return false;
        }
    }
}
