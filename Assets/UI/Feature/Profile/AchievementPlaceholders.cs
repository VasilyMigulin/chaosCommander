using System.Collections.Generic;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// ВРЕМЕННЫЙ источник «последних полученных достижений» для профиля — пока настоящей системы нет.
    /// Все записи считаются полученными (лента = «последние полученные», без заблокированных). Когда появится
    /// реальная система (Game.Core.Progression: определения + прогресс + серверная выдача), ProfilePanel
    /// переключат на неё, а этот класс удалят. Иконки не заданы — в ячейке останется дефолт из префаба.
    /// </summary>
    public static class AchievementPlaceholders
    {
        // «Свежие сверху» — все уже получены.
        static readonly string[] _titles =
        {
            "Первая кровь",
            "Гроза деревень",
            "Коллекционер хлама",
            "Мастер сейв-скама",
            "Диванный тактик",
            "Гроза донатеров",
        };

        /// <summary>Последние N полученных достижений (обрезка/повтор под число слотов).</summary>
        public static IReadOnlyList<string> Recent(int count)
        {
            if (count <= 0) return System.Array.Empty<string>();
            var list = new List<string>(count);
            for (int i = 0; i < count; i++) list.Add(_titles[i % _titles.Length]);
            return list;
        }
    }
}
