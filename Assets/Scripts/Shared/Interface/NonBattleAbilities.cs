using System;
using UnityEngine;

namespace Game.Core.Shared.Interface
{
    // ─────────────────────────────────────────────────────────────────────────
    // Конкретные небоевые способности. Лежат рядом с интерфейсами (Shared.Interface), потому что
    // их обязаны видеть и редактор колод, и ECS-системы матча — общей нижней сборки, кроме этой, нет.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>«Босс качалки Билли»: переопределяет раздачу — стартовая рука N и/или замена любого числа
    /// предложенных карт. РАНЬШЕ это были два поля прямо в CardModel (MulliganStartingHand /
    /// MulliganUnlimitedReplace) — хардкод, потому что боевому Ability мулиган недоступен: он идёт до
    /// первого хода. Теперь это обычная небоевая способность, и новые карты этой механики не требуют
    /// правок модели.</summary>
    [Serializable]
    public sealed class MulliganSetupAbility : IMatchSetupAbility
    {
        [Tooltip("Абсолютный размер стартовой руки (Билли: 4). 0 — не переопределять.")]
        public int StartingHand = 0;

        [Tooltip("Разрешить заменить любое число предложенных карт.")]
        public bool UnlimitedReplace = false;

        public string DebugName => "Стартовая рука/мулиган";

        public void Contribute(ref MatchSetupRules rules)
        {
            if (StartingHand > 0) rules.RaiseStartingHand(StartingHand);
            if (UnlimitedReplace) rules.AllowUnlimitedReplace();
        }
    }

    /// <summary>«Сказочник»: при сборке колоды игрок откладывает Size карт в отдельную зону (сайдборд).
    /// САМА способность только ОБЪЯВЛЯЕТ зону и её размер — она ничего не кладёт в руку. Достаёт карту
    /// из сайдборда обычная БОЕВАЯ способность носителя (раскопка по зоне Sideboard): «отложить» и
    /// «достать» — разные фазы, и смешивать их в одном классе значило бы вернуть хардкод.</summary>
    [Serializable]
    public sealed class SideboardAbility : IDeckBuildAbility
    {
        [Tooltip("Сколько карт игрок откладывает при сборке колоды (Сказочник: 3).")]
        public int Size = 3;

        public string DebugName => $"Сайдборд ({Size})";

        public void Contribute(ref DeckBuildRules rules)
        {
            if (Size > 0) rules.RequireSideboard(Size);
        }
    }
}
