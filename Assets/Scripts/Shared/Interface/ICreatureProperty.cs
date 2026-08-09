using Leopotam.EcsLite;

namespace Game.Core.Shared.Interface
{
    /// <summary>
    /// «Свойство» существа (Двойной удар/Защищённый/...) — аналог кейвордов ХС, НЕ Ability. В отличие от
    /// ICreatureTag (статичная идентичность архетипа) свойство ОБРАТИМО: его можно снять (Remove), поэтому
    /// его же можно раздавать рантаймом через PropertyBuff (IBuffable) + AddBuffEffect — в т.ч. аурой
    /// (Tracked=true), с авто-откатом при смерти источника. Authored как [SerializeReference] в
    /// CardCreatureModel.Properties — печатное свойство карты, применяется на ините.
    /// Каждая реализация владеет СВОИМ ECS-компонентом (DoubleAttackTag/ShieldComponent/...), а не общим
    /// списком ключей — свойства несут разное состояние (заряды/счётчики), в отличие от архетипов.
    /// </summary>
    public interface ICreatureProperty
    {
        /// <summary>Идентификатор свойства ("DoubleAttack"/"Shielded"/...).</summary>
        string Key { get; }

        /// <summary>Выдать свойство сущности.</summary>
        void Apply(EcsWorld world, int entity);

        /// <summary>Снять свойство (откат ауры/баффа).</summary>
        void Remove(EcsWorld world, int entity);

        /// <summary>Есть ли у сущности это свойство сейчас.</summary>
        bool Has(EcsWorld world, int entity);
    }
}
