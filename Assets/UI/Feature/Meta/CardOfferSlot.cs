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
    /// Слот оффера чёрного рынка: ПОЛНАЯ карта + цена + «Купить». Недоступный оффер (в этой ротации уже
    /// что-то куплено) показывает _soldOverlay и не кликается.
    ///
    /// Карту рисует вложенный StaticCardView (КОМПОЗИЦИЯ, не наследование) — тот же вид, что префаб
    /// InspectCardView (арт/стоимость/статы/рамка редкости из CardModel). Панель резолвит CardModel по
    /// offer.ItemId (через CardConfig) и передаёт сюда. Слот отвечает за «магазинные» атрибуты: цена,
    /// «Купить», «Продано».
    ///
    /// Префаб: Button на корне (IsButton=true, EnableIcon=false), _cardView (StaticCardView — на дочернем
    /// объекте-карте, напр. префаб InspectCardView), _priceText ("600 GD"), _soldOverlay.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class CardOfferSlot : SourceSlot
    {
        [Tooltip("Полная карта оффера — StaticCardView на дочернем объекте (напр. префаб InspectCardView).")]
        [SerializeField] private StaticCardView _cardView;
        [SerializeField] private TextMeshProUGUI _priceText;
        [Tooltip("Затемнение/«Продано» — включается, когда оффер недоступен (в этой ротации уже что-то куплено).")]
        [SerializeField] private GameObject _soldOverlay;

        BlackMarketService.Offer _offer;
        Action<BlackMarketService.Offer> _onBuy;
        bool _subscribed;

        public void SetData(BlackMarketService.Offer offer, CardModel model, Action<BlackMarketService.Offer> onBuy)
        {
            _offer = offer;
            _onBuy = onBuy;

            if (_cardView != null && model != null) _cardView.SetModel(model);   // полная карта рисуется отсюда

            if (!_subscribed) { PlayerWallet.OnChanged += UpdateView; _subscribed = true; }
            UpdateView();
        }

        public override void UpdateView()
        {
            if (_offer == null) return;
            if (_priceText != null) _priceText.text = $"{_offer.PriceAmount} {_offer.PriceCode}";
            if (_soldOverlay != null) _soldOverlay.SetActive(!_offer.Available);

            bool affordable = PlayerWallet.Get(_offer.PriceCode) >= _offer.PriceAmount;
            if (_btnClick != null) _btnClick.interactable = _offer.Available && affordable;
        }

        public override void OnClick()
        {
            if (_offer == null || !_offer.Available) return;
            if (PlayerWallet.Get(_offer.PriceCode) < _offer.PriceAmount) return;
            _onBuy?.Invoke(_offer);
        }

        public override void OnUse() { }

        public override void Unject()
        {
            _onBuy = null;
            if (_subscribed) { PlayerWallet.OnChanged -= UpdateView; _subscribed = false; }
        }

        public override void Dispose()
        {
            Unject();
            base.Dispose();
        }
    }
}
