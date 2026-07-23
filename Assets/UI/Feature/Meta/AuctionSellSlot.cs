using System;
using AwesomeUI.Core.Slot;
using Game.Core.Backend;
using Game.Core.Model.Card;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// Слот карты игрока к выставлению на аукцион (вкладка «Мои» → «Продать»). ПОЛНАЯ карта через
    /// StaticCardView (как CardOfferSlot/AuctionListingSlot) + «x4» + ВСТРОЕННЫЕ поля: цена (стартовая
    /// ставка), выбор валюты (золото/гемы) и кнопка «Выставить». Всё прямо в карточке, без отдельного
    /// диалога. Панель получает (itemId, currency, price) через onList и вызывает сервер.
    ///
    /// Префаб: Button на корне (raycastTarget картинки корня можно выключить, чтобы не перехватывать ввод),
    /// _cardView (StaticCardView — префаб InspectCardView), _countText ("x4"), _priceInput (TMP_InputField),
    /// _goldBtn, _gemsBtn, _currencyLabel, _listButton (+ _listLabel на нём).
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class AuctionSellSlot : SourceSlot
    {
        [Tooltip("Полная карта — StaticCardView на дочернем объекте (напр. префаб InspectCardView).")]
        [SerializeField] private StaticCardView _cardView;
        [SerializeField] private TextMeshProUGUI _countText;
        [Tooltip("Стартовая ставка (цена).")]
        [SerializeField] private TMP_InputField _priceInput;
        [SerializeField] private Button _goldBtn;
        [SerializeField] private Button _gemsBtn;
        [Tooltip("Показ выбранной валюты (имя из UIStrings).")]
        [SerializeField] private TextMeshProUGUI _currencyLabel;
        [SerializeField] private Button _listButton;
        [Tooltip("Подпись кнопки «Выставить» (опц.) — код ставит из UIStrings.")]
        [SerializeField] private TextMeshProUGUI _listLabel;

        string _itemId;
        string _currency = BackendConfig.GoldCode;
        Action<string, string, int> _onList;
        bool _wired;

        public void SetData(string itemId, CardModel model, int count, Action<string, string, int> onList)
        {
            _itemId = itemId;
            _onList = onList;
            if (_cardView != null && model != null) _cardView.SetModel(model);   // полная карта рисуется отсюда
            if (_countText != null) _countText.text = $"x{count}";
            if (_priceInput != null) _priceInput.text = "";
            SetCurrency(BackendConfig.GoldCode);   // по умолчанию золото
            if (_listLabel != null) _listLabel.text = UIStrings.AuctionList;

            if (!_wired)
            {
                if (_goldBtn != null) _goldBtn.onClick.AddListener(() => SetCurrency(BackendConfig.GoldCode));
                if (_gemsBtn != null) _gemsBtn.onClick.AddListener(() => SetCurrency(BackendConfig.GemsCode));
                if (_listButton != null) _listButton.onClick.AddListener(OnListClicked);
                _wired = true;
            }
        }

        void SetCurrency(string code)
        {
            _currency = code;
            if (_currencyLabel != null) _currencyLabel.text = UIStrings.CurrencyName(code);
        }

        void OnListClicked()
        {
            int price = 0; if (_priceInput != null) int.TryParse(_priceInput.text, out price);
            if (price <= 0 || string.IsNullOrEmpty(_itemId)) return;
            _onList?.Invoke(_itemId, _currency, price);
        }

        public override void UpdateView() { }
        public override void OnClick() { }   // действие — на кнопке «Выставить», не на всей карточке
        public override void OnUse() { }

        public override void Unject()
        {
            _onList = null;
            if (_wired)
            {
                if (_goldBtn != null) _goldBtn.onClick.RemoveAllListeners();
                if (_gemsBtn != null) _gemsBtn.onClick.RemoveAllListeners();
                if (_listButton != null) _listButton.onClick.RemoveAllListeners();
                _wired = false;
            }
        }

        public override void Dispose()
        {
            Unject();
            base.Dispose();
        }
    }
}
