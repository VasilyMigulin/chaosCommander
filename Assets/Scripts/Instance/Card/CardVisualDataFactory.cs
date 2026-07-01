using Game.Core.Model.Card;
using Game.Core.Model.Card.Creature;
using Game.Core.Model.Card.Charm;
using Game.Core.Shared;
using Game.Core.Service;

namespace Game.Core.Instance.Card
{
    /// <summary>
    /// Создаёт CardVisualData из CardModel.
    /// Живёт в Instance.Card — имеет доступ к моделям карт.
    /// </summary>
    public static class CardVisualDataFactory
    {
        public static CardVisualData From(CardModel model, bool isCommander = false)
        {
            if (model == null) return default;

            var cardType   = model.GetCardType();
            int charmTurns = model is CardCharmModel charm ? charm.TurnsAlive : 0;

            // Имя/описание прогоняем через форматтер: локализация + подстановка *N* (вне боя —
            // числа из текста) + авто-болд ключевых фраз + суффикс длительности для чар.
            string nameKey = CardTextLocalization.NameKey(model.ExpansionId, model.Id);
            string descKey = CardTextLocalization.DescKey(model.ExpansionId, model.Id);

            var data = new CardVisualData
            {
                CardName    = CardDescriptionFormatter.FormatName(nameKey, model.Name),
                Description = CardDescriptionFormatter.Format(descKey, model.Description, cardType, charmTurns, null),
                Icon        = model.ArtImage,
                Rarity      = model.Rarity,
                Element     = model.Element,
                CardType    = cardType,
                CostType    = model.PlayCost,
                CostAmount  = model.PlayCostAmount,
                IsCommander = isCommander,
            };

            if (model is CardCreatureModel creature)
            {
                data.IsCreature = true;
                data.Attack     = creature.Attack;
                data.MaxHealth  = creature.MaxHealth;
                data.Speed      = creature.Speed;
            }

            return data;
        }
    }
}
