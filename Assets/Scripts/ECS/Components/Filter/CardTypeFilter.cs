using Game.Core.Service;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;

namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Фильтр по типу карты: Creature / Spell / Charm.
    /// Проверяется по соответствующему ECS-тегу.
    /// </summary>
    [System.Serializable]
    public struct CardTypeFilter : IEntityFilter
    {
        public EnumService.CardType Type;

        public bool Matches(EcsWorld world, int candidateEntity, int ownerPlayerEntity)
        {
            switch (Type)
            {
                case EnumService.CardType.Creature: return world.GetPool<CreatureTag>().Has(candidateEntity);
                case EnumService.CardType.Spell:    return world.GetPool<SpellTag>().Has(candidateEntity);
                case EnumService.CardType.Charm:    return world.GetPool<CharmTag>().Has(candidateEntity);
            }
            return false;
        }
    }
}
