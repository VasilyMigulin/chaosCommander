using System.Collections.Generic;

namespace Game.Core.Shared.Interface
{
    /// <summary>
    /// Эффект-призыв, несущий модификаторы для призванных существ (бафф/таймер). Лежит в
    /// Shared.Interface, чтобы пассивный реплей (Game.Core.Ecs.Systems) мог достать модификаторы
    /// из своей же копии способности БЕЗ ссылки на Game.Core.Ability (её у Systems нет).
    /// Применяет их ReplayActionSystem к ключам SummonedEntityKeys из снапшота.
    /// </summary>
    public interface ISummonModifierProvider
    {
        IReadOnlyList<IEffect> SummonModifiers { get; }
    }
}
