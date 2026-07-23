using System.Collections.Generic;
using Game.Core.Service;

namespace Game.Core.Shared
{
    /// <summary>
    /// Источник локализованного текста для карт. Реализация по умолчанию (DefaultCardTextProvider)
    /// возвращает авторский фоллбэк и встроенные RU/EN-таблицы служебных фраз — игра работает даже
    /// БЕЗ пакета Unity Localization. Когда таблицы заведены, бэкенд подменяется на
    /// UnityLocalizationCardTextProvider (отдельная сборка, см. setup-доку).
    /// </summary>
    public interface ICardTextProvider
    {
        /// <summary>Код текущего языка: "ru", "en", ...</summary>
        string Language { get; }

        /// <summary>Локализованная строка по ключу; пусто/нет ключа → вернуть fallback (авторский текст).</summary>
        string GetText(string key, string fallback);
    }

    /// <summary>
    /// Глобальный шлюз локализации карт. По умолчанию — passthrough (авторский RU). Бэкенд можно
    /// заменить в рантайме (Provider = ...). Здесь же — служебные RU/EN-таблицы: ключевые фразы для
    /// авто-болда и склонение «Действует N ходов / До конца матча» для чар.
    /// </summary>
    public static class CardTextLocalization
    {
        public static ICardTextProvider Provider = new DefaultCardTextProvider();

        public static string Language => Provider?.Language ?? "ru";

        public static string GetText(string key, string fallback)
            => Provider != null ? Provider.GetText(key, fallback) : fallback;

        // ── Ключи локализации выводятся из идентичности карты — авторить ключи руками не нужно ──
        public static string NameKey(string expansionId, int cardId) => $"card.{expansionId}.{cardId}.name";
        public static string DescKey(string expansionId, int cardId) => $"card.{expansionId}.{cardId}.desc";

        // ── Тип карты (плашка): встроенный RU/EN-фоллбэк + переопределение из таблицы по ключу type.* ──
        public static string TypeLabel(EnumService.CardType type)
        {
            bool en = (Language ?? "ru").StartsWith("en");
            string fallback;
            switch (type)
            {
                case EnumService.CardType.Creature: fallback = en ? "Creature" : "Существо";    break;
                case EnumService.CardType.Spell:    fallback = en ? "Spell"    : "Заклинание"; break;
                case EnumService.CardType.Charm:    fallback = en ? "Charm"    : "Чары";       break;
                default:                            fallback = "";                              break;
            }
            return GetText("type." + type, fallback);
        }

        // ── Ключевые фразы, которые форматтер автоматически делает жирными (<b>…</b>) ──
        // Длинные раньше коротких, чтобы вложенные совпадения не ломали болд.
        static readonly Dictionary<string, string[]> Keywords = new Dictionary<string, string[]>
        {
            ["ru"] = new[]
            {
                "Когда вы получаете урон на своём ходу", "Когда вы получаете урон на своем ходу",
                "При разыгрывании", "В начале матча", "В начале хода", "В конце хода",
                "При получении урона", "При уничтожении", "При убийстве", "При атаке",
                "Пока контролируете", "Когда вы берёте карту", "Когда вы берете карту",
                "По завершении", "При смерти", "При взятии", "При выходе",
            },
            ["en"] = new[]
            {
                "When you take damage during your turn",
                "When played", "At the start of the match", "At the start of turn", "At the end of turn",
                "When damaged", "When destroyed", "On kill", "On attack",
                "While you control", "When you draw a card",
                "When it expires", "On death", "When drawn", "When summoned",
            },
        };

        public static IReadOnlyList<string> KeywordPhrases(string lang)
            => Keywords.TryGetValue(lang, out var arr) ? arr : Keywords["ru"];

        /// <summary>Дефолтная строка длительности чар: 0 ходов → «до конца матча», иначе «Действует N ход(а/ов)».</summary>
        public static string DurationLine(int turnsAlive, string lang)
        {
            bool en = lang == "en";
            if (turnsAlive <= 0)
                return en ? "Until the end of the match" : "До конца матча";

            if (en)
                return turnsAlive == 1 ? "Lasts 1 turn" : $"Lasts {turnsAlive} turns";

            return $"Действует {turnsAlive} {RussianTurns(turnsAlive)}";
        }

        // Склонение «ход/хода/ходов» по числу.
        static string RussianTurns(int n)
        {
            int mod100 = n % 100;
            if (mod100 >= 11 && mod100 <= 14) return "ходов";
            switch (n % 10)
            {
                case 1:           return "ход";
                case 2: case 3: case 4: return "хода";
                default:          return "ходов";
            }
        }
    }

    /// <summary>
    /// Passthrough-провайдер: всегда отдаёт авторский фоллбэк (текст карт — RU). Язык берёт из
    /// LocaleService — поэтому даже без пакета локализации переключение языка флипает служебные
    /// фразы (болд/«Действует N ходов») на EN. Работает без Unity Localization.
    /// </summary>
    public sealed class DefaultCardTextProvider : ICardTextProvider
    {
        public string Language => LocaleService.Current;
        public string GetText(string key, string fallback) => fallback;
    }
}
