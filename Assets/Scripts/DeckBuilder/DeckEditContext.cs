namespace Game.Core.DeckBuilder
{
    // === helper ===
    /// <summary>
    /// Что открыть в DeckBuildPanel. Мост между DeckViewPanel (список колод) и DeckBuildPanel
    /// (редактор): панели связаны только id'шником в AwesomeButton, прямых ссылок друг на друга нет,
    /// поэтому «что редактируем» передаём через этот статик.
    ///
    /// Три сценария:
    ///   • Edit(deck)   — клик по слоту: редактируем существующую (OriginalName нужен, чтобы
    ///                    переименование заменяло колоду, а не плодило дубль);
    ///   • Import(deck) — код колоды из буфера обмена: собираем новую по коду;
    ///   • CreateNew()  — кнопка «добавить», в буфере кода нет: пустой редактор.
    ///
    /// Редактор ЗАБИРАЕТ контекст при открытии (Consume) — второе открытие «на пустом месте»
    /// не подтянет прошлую колоду.
    /// </summary>
    public static class DeckEditContext
    {
        public static SavedDeckData Deck { get; private set; }

        /// <summary>Имя колоды на момент открытия. Пусто → колода новая (сохранение добавит её).</summary>
        public static string OriginalName { get; private set; }

        /// <summary>Колода приехала кодом из буфера обмена (редактор об этом сообщит игроку).</summary>
        public static bool FromCode { get; private set; }

        public static void Edit(SavedDeckData deck)
        {
            Deck = deck;
            OriginalName = deck?.Name;
            FromCode = false;
        }

        public static void Import(SavedDeckData deck)
        {
            Deck = deck;
            OriginalName = null;   // импорт = новая колода, существующую не затираем
            FromCode = true;
        }

        public static void CreateNew()
        {
            Deck = null;
            OriginalName = null;
            FromCode = false;
        }

        /// <summary>Забрать и сбросить (зовёт редактор при открытии).</summary>
        public static SavedDeckData Consume(out string originalName, out bool fromCode)
        {
            var deck = Deck;
            originalName = OriginalName;
            fromCode = FromCode;
            CreateNew();
            return deck;
        }
    }
}
