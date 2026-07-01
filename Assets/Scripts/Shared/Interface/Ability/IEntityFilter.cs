using Leopotam.EcsLite;

namespace Game.Core.Shared.Interface
{
    /// <summary>
    /// Один компонент-фильтр внутри HasMatchingEntityRule. Реализации (struct'ы)
    /// собираются в полиморфный List<IEntityFilter>; правило проходит по миру
    /// и проверяет каждую сущность-кандидата всеми фильтрами через Matches.
    /// </summary>
    public interface IEntityFilter
    {
        bool Matches(EcsWorld world, int candidateEntity, int ownerPlayerEntity);
    }
}
