using System.Collections.Generic;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// ВРЕМЕННЫЙ источник «последних достижений» для профиля — пока настоящей системы достижений нет.
    /// Возвращает фиксированный список плейсхолдеров в духе «Горе-героев». Когда появится реальная система
    /// (в Game.Core.Progression: определения + прогресс + серверная выдача), ProfilePanel переключат на неё,
    /// а этот класс удалят. Иконки не заданы (в ячейке останется дефолт из префаба).
    /// </summary>
    public static class AchievementPlaceholders
    {
        public struct Entry
        {
            public string Title;
            public bool   Earned;
        }

        // Порядок = «свежие сверху». Первые несколько помечены полученными — чтобы в ленте были видны оба
        // состояния (получено / не получено).
        static readonly Entry[] _stub =
        {
            new Entry { Title = "Первая кровь",       Earned = true  },
            new Entry { Title = "Гроза деревень",     Earned = true  },
            new Entry { Title = "Коллекционер хлама", Earned = true  },
            new Entry { Title = "Мастер сейв-скама",  Earned = false },
            new Entry { Title = "Диванный тактик",    Earned = false },
        };

        /// <summary>Последние N достижений (обрезка/повтор под запрошенное число слотов).</summary>
        public static IReadOnlyList<Entry> Recent(int count)
        {
            if (count <= 0) return System.Array.Empty<Entry>();
            var list = new List<Entry>(count);
            for (int i = 0; i < count; i++) list.Add(_stub[i % _stub.Length]);
            return list;
        }
    }
}
