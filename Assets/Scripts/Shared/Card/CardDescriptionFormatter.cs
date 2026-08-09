using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Game.Core.Service;

namespace Game.Core.Shared
{
    /// <summary>
    /// Превращает авторский ШАБЛОН описания в финальный rich-text для TMP. Три прохода:
    ///   1. Локализация — берём строку текущего языка по ключу (фоллбэк = авторский текст).
    ///   2. Динамические значения *N* → живое число эффекта (или N из текста вне боя). Звёздочки убираем.
    ///   3. Авто-болд ВСЕХ триггеров: фраза до двоеточия («В начале хода:», «При смерти:», …, на любом
    ///      языке) + фразы БЕЗ двоеточия из списка (Когда вы получаете урон…, Пока контролируете…).
    ///   4. Для чар — дописываем строку длительности («Действует N ходов» / «До конца матча»).
    ///
    /// Чистый текст: НЕ зависит от ECS/Model. Живые значения (liveValues) собирает вызывающий код
    /// в бою (см. CardDynamicValues в ECS-сборке); вне боя передаём null → берём числа из текста.
    /// </summary>
    public static class CardDescriptionFormatter
    {
        // *N*, *-N* и *+N* — токен динамического значения. Звёздочки в выводе не показываем; авторский
        // «+» (бафф-нотация «*+1* к скорости», Профсоюзное знамя) сохраняется и у живого значения.
        static readonly Regex DynamicToken = new Regex(@"\*([+-]?\d+)\*", RegexOptions.Compiled);

        // Триггер-префикс: любая фраза «…:» в начале клаузы (начало строки, после . ! ? … или переноса).
        // В карточном вординге триггер ВСЕГДА идёт как «<триггер>: <эффект>» — болдим фразу до двоеточия,
        // так авто-болдятся ВСЕ триггеры, без ручного списка и на любом языке. trig не содержит : < > и
        // переносов и не начинается с пробела (чтобы не захватить разделитель предложений).
        static readonly Regex TriggerPrefix = new Regex(
            @"(?:^|(?<=[.!?…]\s)|(?<=\n))(?<trig>[^\s:<>\n][^:<>\n]{1,59}?)\s*:",
            RegexOptions.Compiled);

        /// <summary>Имя карты — только локализация (без шаблонизации).</summary>
        public static string FormatName(string nameKey, string fallbackName)
            => CardTextLocalization.GetText(nameKey, fallbackName ?? string.Empty);

        /// <summary>Строка-префикс из ярлыков свойств (Двойной удар/Защитник/...) — ВСЕГДА в начале описания,
        /// независимо от авторского текста карты (как кейворды в ХС). Собирается ОТДЕЛЬНО от Format и
        /// приклеивается вызывающим ДО тела: сам Format уже прогнал тело через авто-болд, а тот бы молча
        /// пропустил остальные триггеры карты, увидев чужой "&lt;b&gt;" в начале строки (см. BoldKeywords).
        /// Пусто → пустая строка (ничего не приклеивать).</summary>
        public static string BuildPropertyPrefix(IReadOnlyList<string> propertyKeys)
        {
            if (propertyKeys == null || propertyKeys.Count == 0) return string.Empty;
            var labels = new List<string>();
            foreach (var key in propertyKeys)
            {
                if (string.IsNullOrEmpty(key)) continue;
                labels.Add(CardTextLocalization.PropertyLabel(key));
            }
            if (labels.Count == 0) return string.Empty;
            return "<b>" + string.Join(", ", labels) + "</b>\n";
        }

        /// <summary>
        /// Полное описание карты для показа.
        /// descKey/fallbackTemplate — ключ локализации и авторский русский (фоллбэк).
        /// cardType — для авто-суффикса чар; charmTurnsAlive — из CardCharmModel.TurnsAlive.
        /// liveValues — плоский список живых значений эффектов по порядку (null вне боя).
        /// </summary>
        public static string Format(
            string descKey,
            string fallbackTemplate,
            EnumService.CardType cardType,
            int charmTurnsAlive,
            IReadOnlyList<int> liveValues = null)
        {
            string lang = CardTextLocalization.Language;
            string template = CardTextLocalization.GetText(descKey, fallbackTemplate ?? string.Empty);

            string body = SubstituteDynamicValues(template, liveValues);
            body = BoldKeywords(body, lang);

            if (cardType == EnumService.CardType.Charm)
            {
                string duration = CardTextLocalization.DurationLine(charmTurnsAlive, lang);
                if (!string.IsNullOrEmpty(duration))
                    body = string.IsNullOrEmpty(body) ? duration : body + "\n" + duration;
            }

            return body;
        }

        // ── Проход 2: *N* → живое значение (или N из текста) ──
        static string SubstituteDynamicValues(string template, IReadOnlyList<int> liveValues)
        {
            if (string.IsNullOrEmpty(template)) return string.Empty;

            int slot = 0;
            return DynamicToken.Replace(template, m =>
            {
                string raw = m.Groups[1].Value;
                int value;
                if (liveValues != null && slot < liveValues.Count)
                    value = liveValues[slot];
                else
                    int.TryParse(raw, out value);   // фоллбэк = число из текста ("+1" парсится в 1)
                slot++;
                // Автор написал «*+1*» → показываем со знаком и живое значение («+3»), пока оно не ушло в минус.
                bool keepPlus = raw.Length > 0 && raw[0] == '+' && value >= 0;
                return keepPlus ? "+" + value : value.ToString();
            });
        }

        // ── Проход 3: авто-болд триггеров ──
        static string BoldKeywords(string text, string lang)
        {
            if (string.IsNullOrEmpty(text)) return text;
            // Если автор уже расставил болд вручную — не трогаем, чтобы не было вложенных <b>.
            if (text.Contains("<b>")) return text;

            // 1) ОБЩЕЕ ПРАВИЛО: любой триггер-префикс «<фраза>:» → болдим фразу (двоеточие вне болда).
            //    Ловит все триггеры вида «В начале хода:», «При взятии:», «При смерти:», … на любом языке.
            text = TriggerPrefix.Replace(text, "<b>${trig}</b>:");

            // 2) Триггеры/фразы БЕЗ двоеточия (Когда вы получаете урон…, Пока контролируете…) — из списка,
            //    только первое вхождение и только если оно ещё не в болде (не после только что вставленного <b>).
            foreach (var phrase in CardTextLocalization.KeywordPhrases(lang))
            {
                int idx = text.IndexOf(phrase, System.StringComparison.Ordinal);
                if (idx < 0) continue;
                if (idx >= 3 && text.Substring(idx - 3, 3) == "<b>") continue;   // уже заболжено правилом (1)
                var sb = new StringBuilder(text.Length + 7);
                sb.Append(text, 0, idx).Append("<b>").Append(phrase).Append("</b>")
                  .Append(text, idx + phrase.Length, text.Length - idx - phrase.Length);
                text = sb.ToString();
            }
            return text;
        }
    }
}
