using Game.Core.Service;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;

namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Фильтр по зоне (Hand / Deck / Board / Graveyard / Banish).
    /// Zone — flags, можно искать в нескольких зонах одним фильтром.
    /// </summary>
    [System.Serializable]
    public struct ZoneFilter : IEntityFilter
    {
        public EnumService.Zone Zone;

        public bool Matches(EcsWorld world, int candidateEntity, int ownerPlayerEntity)
        {
            if ((Zone & EnumService.Zone.Hand) != 0      && world.GetPool<HandTag>().Has(candidateEntity))  return true;
            if ((Zone & EnumService.Zone.Deck) != 0      && world.GetPool<DeckTag>().Has(candidateEntity))  return true;
            if ((Zone & EnumService.Zone.Board) != 0     && world.GetPool<BoardTag>().Has(candidateEntity)) return true;
            if ((Zone & EnumService.Zone.Graveyard) != 0 && world.GetPool<GraveTag>().Has(candidateEntity)) return true;
            // Banish: тега пока нет в проекте, no-op
            return false;
        }
    }
}
