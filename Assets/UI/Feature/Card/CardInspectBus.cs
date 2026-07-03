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
        public static event Action<CardModel> Requested;

        public static void Request(CardModel model)
        {
            if (model != null) Requested?.Invoke(model);
        }
    }
}
