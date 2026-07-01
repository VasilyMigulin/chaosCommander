using Leopotam.EcsLite;

namespace Game.Core.Shared.Interface
{
    /// <summary>
    /// Фильтр цели (лист) — НЕ путать с play-правилами (IRule). Проверяет КАНДИДАТА-цель:
    /// враг/союзник/существо/игрок/цвет и т.п. Используется и Target-, и Field-способностями.
    /// casterCard/casterPlayer — источник (для определения «враг/союзник»).
    /// </summary>
    public interface ITargetFilter
    {
        bool Match(EcsWorld world, int candidate, int casterCard, int casterPlayer);
    }
}
