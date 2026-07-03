using AwesomeUI.Core.Card;
using Game.Core.Instance.Card;
using Game.Core.Model.Card;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// Display-only карта (без клика/драга) для попапов и списков: крупная карта в предпросмотре
    /// и элементы «связанных карт». Рендерится из CardModel через CardVisualDataFactory.
    /// Удержание работает и здесь (наследуется из CardBaseView) → можно провалиться в связанную карту.
    /// </summary>
    public class StaticCardView : CardBaseView
    {
        public CardModel Model { get; private set; }

        public void SetModel(CardModel model, bool isCommander = false)
        {
            Model = model;
            if (model != null)
                SetVisual(CardVisualDataFactory.From(model, isCommander));
        }

        protected override void OnHoldTriggered()
        {
            if (Model != null) CardInspectBus.Request(Model);
        }

        public override void OnUse() { }
        public override void OnClick() { }
        public override void Unject() { }
        public override void UpdateView() { }
    }
}
