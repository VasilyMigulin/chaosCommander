using System.Collections.Generic;
using Game.Core.Model.Card;
using Game.Core.Shared;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// Определяет, какие блоки-подсказки механик (KeywordHintBlockView) показать рядом с картой в
    /// CardInspectPopup. Тексты — из локализации по ключам ui.keyword.{id}.name / ui.keyword.{id}.desc
    /// (card_text.csv): подсказка показывается ТОЛЬКО если у ключа есть описание — так новые механики
    /// подключаются добавлением пары строк локализации, без правок кода.
    ///
    /// Источники ключей сейчас: архетипы карты (Archetypes → ICreatureTag.Key: "Imp"/"Worker"/…).
    /// БУДУЩИЕ механики (Защитник/провокация, Двойной удар и т.п.): когда появятся их маркеры на
    /// CardModel/способностях — добавить сюда ветку, которая мапит маркер в id ключа локализации.
    /// </summary>
    public static class KeywordHintsResolver
    {
        public readonly struct KeywordHint
        {
            public readonly string Title;
            public readonly string Description;
            public KeywordHint(string title, string description) { Title = title; Description = description; }
        }

        public static List<KeywordHint> Resolve(CardModel source)
        {
            var hints = new List<KeywordHint>();
            if (source == null) return hints;

            if (source.Archetypes != null)
                foreach (var tag in source.Archetypes)
                {
                    if (tag == null || string.IsNullOrEmpty(tag.Key)) continue;
                    TryAdd(hints, tag.Key.ToLowerInvariant(), fallbackTitle: tag.Key);
                }

            // TODO (будущие механики): Защитник/Двойной удар и др. — по их маркерам, тем же TryAdd(id).

            return hints;
        }

        static void TryAdd(List<KeywordHint> hints, string id, string fallbackTitle)
        {
            string desc = CardTextLocalization.GetText($"ui.keyword.{id}.desc", "");
            if (string.IsNullOrEmpty(desc)) return;   // нет описания в локализации → механика ещё «не задокументирована», блок не показываем
            string title = CardTextLocalization.GetText($"ui.keyword.{id}.name", fallbackTitle);
            hints.Add(new KeywordHint(title, desc));
        }
    }
}
