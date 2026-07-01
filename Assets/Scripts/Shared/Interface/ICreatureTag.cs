using Leopotam.EcsLite;

namespace Game.Core.Shared.Interface
{
    /// <summary>
    /// Архетип существа (работяга/чёрт/...). Authored как [SerializeReference] в CardModel.Archetypes.
    /// Каждый САМ вешает себя на ините (Apply → ключ в ArchetypeComponent) и умеет проверить наличие (Has).
    /// Key — строковый идентификатор архетипа: его же берут ArchetypeTargetFilter и счётчики (RepeatEffect/
    /// AbilityCount) — поэтому отдельно прописывать ключ не нужно, достаточно дать сам ICreatureTag.
    /// </summary>
    public interface ICreatureTag
    {
        /// <summary>Идентификатор архетипа ("Imp"/"Worker"/...).</summary>
        string Key { get; }

        /// <summary>Повесить себя (Key) на сущность карты (вызывается из CardModel.Init).</summary>
        void Apply(EcsWorld world, int cardEntity);

        /// <summary>Есть ли у сущности этот архетип.</summary>
        bool Has(EcsWorld world, int entity);
    }
}
