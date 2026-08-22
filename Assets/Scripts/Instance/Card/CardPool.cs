using System.Collections.Generic;
using Game.Core.Service;
using Game.Core.Model.Card;
using Game.Core.Model.Card.Charm;
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

        [Tooltip("Включать карты из StoryOnly-экспаншенов (сюжетные боссы/эксклюзивы — ExpansionConfig.StoryOnly, " +
                 "PlayerLibrary.FillFullCollection их тоже пропускает). Выкл по умолчанию — такие карты не для " +
                 "случайной генерации/дискавера вне сюжета.")]
        public bool IncludeStoryOnly = false;

        [Tooltip("Фильтр по длительности ЧАРЫ (TurnsAlive). На существ/заклинания не влияет — просто " +
                 "игнорируется, если карта не чара (сочетай с FilterType=Charm, если нужен пул только из чар).")]
        public bool FilterCharmDuration = false;
        public enum CharmDurationKind { TemporaryOnly = 0, PermanentOnly = 1 }
        [Tooltip("TemporaryOnly — исключить вечные чары (TurnsAlive == 0, «до конца матча»). PermanentOnly — " +
                 "наоборот, оставить только вечные.")]
        public CharmDurationKind CharmDuration = CharmDurationKind.TemporaryOnly;

        [Header("Запечённый список (заполняется кнопкой Rebuild)")]
        public List<CardInstanceData> Cards = new List<CardInstanceData>();

        IReadOnlyList<ICreatable> ICardPool.Cards => Cards;

        /// <summary>Подходит ли карта под критерии. assetPath — путь ассета (для фильтра по экспанжену в редакторе,
        /// т.к. CardModel.ExpansionId заполняется только в рантайме). isStoryOnly — карта принадлежит
        /// ExpansionConfig.StoryOnly (считает CardPoolEditor.Rebuild по ExpansionConfig.Cards — членство
        /// авторитетно, в отличие от пути/CardModel.ExpansionId, который у части карт не заполнен).
        /// Вызывает CardPoolEditor.Rebuild.</summary>
        public bool Matches(CardModel m, string assetPath, bool isStoryOnly = false)
        {
            if (m == null) return false;
            if (!IncludeStoryOnly && isStoryOnly) return false;
            if (!IncludeTokens && m.IsToken) return false;
            if (ColorMask != 0 && (m.Element & ColorMask) == 0) return false;
            if (FilterType && m.GetCardType() != Type) return false;
            if (FilterCost && (m.PlayCost != CostType || m.PlayCostAmount < MinCost || m.PlayCostAmount > MaxCost)) return false;
            if (FilterRarity && m.Rarity != Rarity) return false;
            if (FilterCharmDuration && m is CardCharmModel charmModel)
            {
                bool isPermanent = charmModel.TurnsAlive == 0;
                if (CharmDuration == CharmDurationKind.TemporaryOnly && isPermanent) return false;
                if (CharmDuration == CharmDurationKind.PermanentOnly && !isPermanent) return false;
            }
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
