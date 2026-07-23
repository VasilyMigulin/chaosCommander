using AwesomeUI.Core.Card;
using Game.Core.Shared;

namespace AwesomeUI.Feature.Battle
{
    /// <summary>
    /// Миниатюра одного розыгрыша в истории (PlayHistoryDrawerView) — ЛЮБОЙ тип карты (существо/спелл/чара).
    /// Обычная CardBaseView-карта — переиспользует уже готовый hold-детект базового класса: удержание
    /// показывает ТОТ ЖЕ попап (CardDetailPopupView), что и удержание на существе на столе (единый попап,
    /// не два разных — тип карты ему не важен, он просто рендерит CardVisualData).
    /// </summary>
    public class PlayHistoryThumbView : CardBaseView
    {
        CardDetailPopupView _detailPopup;
        CardVisualData _visual;
        bool _hasData;

        public override void Unject()     { }
        public override void OnUse()      { }
        public override void OnClick()    { ConsumeHoldClick(); }   // тап по миниатюре — ничего, просто гасим hold-click
        public override void UpdateView() { }

        /// <summary>Заполнить слот данными розыгрыша (detailPopup — общая ссылка, одна на весь дровер).</summary>
        public void Setup(CardDetailPopupView detailPopup, in CardVisualData visual)
        {
            _detailPopup = detailPopup;
            _visual = visual;
            _hasData = true;
            ApplyVisualData(visual);
            gameObject.SetActive(true);
        }

        /// <summary>Пустой слот (розыгрышей ещё меньше, чем слотов).</summary>
        public void Clear()
        {
            _hasData = false;
            gameObject.SetActive(false);
        }

        protected override void OnHoldTriggered()
        {
            if (_hasData) _detailPopup?.Show(_visual);
        }

        protected override void OnHoldReleased() => _detailPopup?.Hide();
    }
}
