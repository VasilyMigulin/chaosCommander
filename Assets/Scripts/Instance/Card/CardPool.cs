using System.Collections.Generic;
using Game.Core.Service;
using Game.Core.Model.Card;
using Game.Core.Shared.Interface;
using UnityEngine;

namespace Game.Core.Instance.Card
{
    /// <summary>
    /// Пул карт, собираемый ПО КРИТЕРИЯМ в редакторе (кнопка Rebuild в инспекторе → CardPoolEditor сканирует
    /// все CardInstanceData и заполняет Cards). Эффекты-роллеры ссылаются на него (ICardPool) и берут случайную
    /// карту из запечённого Cards. Запечено намеренно: в игре не сканируем (перф на ~1000 карт) + список
    /// одинаков у обоих клиентов (синк). Добавил/поменял карты → нажми Rebuild.
    /// </summary>
    [CreateAssetMenu(fileName = "CardPool", menuName = "Data/CardPool")]
    public class CardPool : ScriptableObject, ICardPool
    {
        [Header("Критерии (пустые/выключенные — не фильтруют)")]
        [Tooltip("Цвет (флаги). 0 = любой. Матч: карта содержит ЛЮБОЙ из выбранных цветов.")]
        public EnumService.Element ColorMask = 0;

        public bool FilterType = false;
        public EnumService.CardType Type = EnumService.CardType.Spell;

        public bool FilterCost = false;
        public EnumService.ResourceType CostType = EnumService.ResourceType.Gold;
        public int MinCost = 0;
        public int MaxCost = 99;

        public bool FilterRarity = false;
        public EnumService.Rarity Rarity = EnumService.Rarity.Common;

        [Tooltip("Архетип (перетащить тег). Пусто = без фильтра. Матч по Key (любой архетип карты совпадает).")]
        [SerializeReference] public ICreatureTag Archetype;

        [Tooltip("Включать токены (IsToken). По умолчанию выкл — пулы обычно из обычных карт.")]
        public bool IncludeTokens = false;

        [Tooltip("ExpansionId (пусто = все). Матч по пути ассета .../Expansion/<ExpansionId>/.")]
        public string ExpansionId = "";

        [Header("Запечённый список (заполняется кнопкой Rebuild)")]
        public List<CardInstanceData> Cards = new List<CardInstanceData>();

        IReadOnlyList<ICreatable> ICardPool.Cards => Cards;

        /// <summary>Подходит ли карта под критерии. assetPath — путь ассета (для фильтра по экспанжену в редакторе,
        /// т.к. CardModel.ExpansionId заполняется только в рантайме). Вызывает CardPoolEditor.Rebuild.</summary>
        public bool Matches(CardModel m, string assetPath)
        {
            if (m == null) return false;
            if (!IncludeTokens && m.IsToken) return false;
            if (ColorMask != 0 && (m.Element & ColorMask) == 0) return false;
            if (FilterType && m.GetCardType() != Type) return false;
            if (FilterCost && (m.PlayCost != CostType || m.PlayCostAmount < MinCost || m.PlayCostAmount > MaxCost)) return false;
            if (FilterRarity && m.Rarity != Rarity) return false;
            if (Archetype != null)
            {
                bool has = m.Archetypes != null && m.Archetypes.Exists(a => a != null && a.Key == Archetype.Key);
                if (!has) return false;
            }
            if (!string.IsNullOrEmpty(ExpansionId) &&
                (assetPath == null ||
                 assetPath.IndexOf("/Expansion/" + ExpansionId + "/", System.StringComparison.OrdinalIgnoreCase) < 0))
                return false;
            return true;
        }
    }
}
