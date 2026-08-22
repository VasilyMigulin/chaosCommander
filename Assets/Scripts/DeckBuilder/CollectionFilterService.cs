using Game.Core.Model.Card;
using Game.Core.Service;

namespace Game.Core.DeckBuilder
{
    /// <summary>
    /// Состояние и логика доп. фильтров библиотеки в DeckBuildPanel (цвет/тип/аддон/стоимость) — активны,
    /// пока в редакторе не выбран командир (см. DeckBuildPanel.RefreshLibrary/RefreshMeta).
    /// Не зависит от ECS/MonoBehaviour/UI — чистая проверка карты по цвету/типу/аддону/стоимости.
    /// Поиск по имени сюда намеренно не входит — его уже делает DeckBuildPanel.MatchesSearch,
    /// дублировать не нужно. Разблокировку аддонов (CampaignProgress) сюда тоже не кладём: этот класс
    /// живёт в сборке Game.Core.DeckBuilder, которая НЕ ссылается на AwesomeUI.Feature — этой логикой
    /// владеет CollectionFilterBarView (UI-слой, см. GetUnlockedExpansions/DefaultExpansionId там).
    /// </summary>
    public sealed class CollectionFilterService
    {
        /// <summary>0 = цветовой фильтр выключен, показываем карты любого цвета.</summary>
        public EnumService.Element ColorMask;

        /// <summary>null = любой тип карты.</summary>
        public EnumService.CardType? TypeFilter;

        public string ExpansionId;

        public int CostMin = 0;
        public int CostMax = 10;

        public bool InSelectedExpansion(CardModel model)
            => model != null && model.ExpansionId == ExpansionId;

        /// <summary>Цвет + тип + аддон + стоимость. Поиск по имени — отдельно, см. класс.</summary>
        public bool Matches(CardModel model)
        {
            if (model == null) return false;
            if (!InSelectedExpansion(model)) return false;
            if (ColorMask != 0 && (model.Element & ColorMask) == 0) return false;
            if (TypeFilter.HasValue && model.GetCardType() != TypeFilter.Value) return false;
            if (model.PlayCostAmount < CostMin || model.PlayCostAmount > CostMax) return false;
            return true;
        }
    }
}
