using System.Collections.Generic;

namespace Game.Core.Shared.Interface
{
    // === helper (static) ===
    /// <summary>
    /// ЕДИНСТВЕННОЕ место, где небоевые способности превращаются в правила. Принимает плоский поток
    /// способностей (вызывающий сам решает, чьи это карты — вся колода, только командир, колода+сайдборд),
    /// поэтому не тянет за собой CardModel и остаётся в нижней сборке, видимой и редактору колод, и ECS.
    ///
    /// Пустой/нулевой вход даёт агрегат по умолчанию — «правил нет», а не исключение: колода без единой
    /// небоевой карты это норма, и вызывающему не нужно об этом думать.
    /// </summary>
    public static class NonBattleRules
    {
        /// <summary>Правила СБОРКИ колоды по всем картам колоды (командир входит).</summary>
        public static DeckBuildRules Deck(IEnumerable<INonBattleAbility> abilities)
        {
            var rules = default(DeckBuildRules);
            if (abilities == null) return rules;
            foreach (var a in abilities)
                if (a is IDeckBuildAbility d) d.Contribute(ref rules);
            return rules;
        }

        /// <summary>Правила ПОДГОТОВКИ матча по всем картам колоды (командир входит).</summary>
        public static MatchSetupRules Match(IEnumerable<INonBattleAbility> abilities)
        {
            var rules = default(MatchSetupRules);
            if (abilities == null) return rules;
            foreach (var a in abilities)
                if (a is IMatchSetupAbility m) m.Contribute(ref rules);
            return rules;
        }
    }
}
