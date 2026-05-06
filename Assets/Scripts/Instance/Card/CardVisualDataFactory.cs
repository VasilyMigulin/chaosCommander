using Game.Core.Model.Card;
using Game.Core.Model.Card.Creature;
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

            var data = new CardVisualData
            {
                CardName    = model.Name,
                Description = model.Description,
                Icon        = model.Icon,
                Rarity      = model.Rarity,
                Element     = model.Element,
                CardType    = model.GetCardType(),
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
