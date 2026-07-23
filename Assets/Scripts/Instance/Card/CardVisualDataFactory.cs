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
        /// <summary>Из CardViewDataComponent (уже собранные при инициализации entity данные — без похода
        /// в CardConfig/CardModel). Поля 1:1 совпадают с CardVisualData — раньше копировались вручную в
        /// ReplayActionSystem.ReplayCast и PveOpponentCardPlayUISystem.OnCast (дублирование), теперь один
        /// хелпер на оба места + на новые фичи (инспект существа, история розыгрышей).</summary>
        public static CardVisualData From(in Game.Core.Ecs.Components.CardViewDataComponent vd)
        {
            return new CardVisualData
            {
                CardName    = vd.CardName,
                Description = vd.Description,
                Icon        = vd.ArtImage,
                CardType    = vd.CardType,
                Rarity      = vd.Rarity,
                Element     = vd.Element,
                CostType    = vd.CostType,
                CostAmount  = vd.CostAmount,
                IsCreature  = vd.IsCreature,
                Attack      = vd.Attack,
                MaxHealth   = vd.MaxHealth,
                Speed       = vd.Speed,
                IsCommander = vd.IsCommander,
            };
        }

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
