using System.Collections.Generic;

namespace Game.Core.Shared.Interface
{
    /// <summary>
    /// Пул карт, собранный по критериям в редакторе (CardPool ScriptableObject) и запечённый в список.
    /// Эффекты-роллеры (PlayRandomFromPool/GainRandomCard/SpawnRandomCard) ссылаются на него как на
    /// ScriptableObject и читают Cards через этот интерфейс — БЕЗ ссылки на CardInstanceData (нет цикла сборок).
    /// Запечённость = быстрый ролл в игре + детерминизм/синк (список одинаков у обоих клиентов).
    /// </summary>
    public interface ICardPool
    {
        IReadOnlyList<ICreatable> Cards { get; }
    }
}
