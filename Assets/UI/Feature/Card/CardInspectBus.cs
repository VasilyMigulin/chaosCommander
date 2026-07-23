using System;
using Game.Core.Model.Card;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// Глобальный канал запроса предпросмотра карты. Карточные вьюшки (CardBaseView) шлют сюда
    /// модель при удержании; CardInspectPopup слушает и показывает окно. Разъединяет вьюшки и попап.
    /// </summary>
    public static class CardInspectBus
    {
        /// <summary>bool = allowDust: показывать ли кнопку «Порвать» (только из коллекции игрока).</summary>
        public static event Action<CardModel, bool> Requested;

        /// <summary>Обычный осмотр (бой/статик-вьюхи) — БЕЗ «Порвать».</summary>
        public static void Request(CardModel model) => Raise(model, false);

        /// <summary>Осмотр из КОЛЛЕКЦИИ (DeckCardView/LibraryCardView) — с кнопкой «Порвать».</summary>
        public static void RequestDustable(CardModel model) => Raise(model, true);

        static void Raise(CardModel model, bool allowDust)
        {
            if (model != null) Requested?.Invoke(model, allowDust);
        }
    }
}
