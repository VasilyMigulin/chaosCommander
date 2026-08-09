using System.Collections.Generic;
using Game.Core.Model.Card;
using Game.Core.Model.Card.Creature;
using Game.Core.Shared;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// Определяет, какие блоки-подсказки механик (KeywordHintBlockView) показать рядом с картой в
    /// CardInspectPopup. Тексты — из локализации по ключам ui.keyword.{id}.name / ui.keyword.{id}.desc
    /// (card_text.csv): подсказка показывается ТОЛЬКО если у ключа есть описание — так новые механики
    /// подключаются добавлением пары строк локализации, без правок кода.
    ///
    /// Источники ключей: архетипы карты (Archetypes → ICreatureTag.Key: "Imp"/"Worker"/…) и свойства
    /// существ (Properties → ICreatureProperty.Key: "Taunt"/"DoubleAttack"/…, кейворды ХС-типа —
    /// см. AbilityProperties.cs). Тот же id (в нижнем регистре) используют CardTextLocalization.PropertyLabel
    /// (короткий ярлык в самом описании) и ui.keyword.{id}.* (развёрнутая подсказка-reminder-text здесь).
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

            if (source is CardCreatureModel creature && creature.Properties != null)
                foreach (var prop in creature.Properties)
                {
                    if (prop == null || string.IsNullOrEmpty(prop.Key)) continue;
                    TryAdd(hints, prop.Key.ToLowerInvariant(), fallbackTitle: CardTextLocalization.PropertyLabel(prop.Key));
                }

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
