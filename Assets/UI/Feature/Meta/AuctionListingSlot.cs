using System;
using System.Globalization;
using AwesomeUI.Core.Slot;
using Game.Core.Backend;
using Game.Core.Model.Card;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// Слот лота аукциона (модель ставок): ПОЛНАЯ карта + текущая ставка (валюта) + таймер + продавец +
    /// статус (лидируете/перебили/ставок нет) + кнопка действия.
    ///
    /// Карту рисует вложенный StaticCardView (КОМПОЗИЦИЯ, как у CardOfferSlot на чёрном рынке) — тот же вид,
    /// что префаб InspectCardView. Панель резолвит CardModel по lot.ItemId (через CardConfig) и передаёт сюда.
    /// Слот отвечает за «аукционные» атрибуты: ставка, таймер, продавец, статус, действие.
    /// Чужой лот → «Ставка»/«Перебить»; свой без ставок → «Снять»; истёкший → «Завершён» (кнопка гаснет).
    ///
    /// Префаб: Button на корне (IsButton=true, EnableIcon=false), _cardView (StaticCardView — префаб
    /// InspectCardView на дочернем объекте), _bidText (число), _bidCurrencyIcon (Image), _bidLabel (Старт/Ставка),
    /// _timerText, _sellerText, _statusText, _actionLabel (TMP на кнопке).
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class AuctionListingSlot : SourceSlot
    {
        [Tooltip("Полная карта лота — StaticCardView на дочернем объекте (напр. префаб InspectCardView).")]
        [SerializeField] private StaticCardView _cardView;
        [SerializeField] private TextMeshProUGUI _bidText;
        [SerializeField] private Image _bidCurrencyIcon;
        [SerializeField] private TextMeshProUGUI _bidLabel;
        [SerializeField] private TextMeshProUGUI _timerText;
        [SerializeField] private TextMeshProUGUI _sellerText;
        [SerializeField] private TextMeshProUGUI _statusText;
        [SerializeField] private TextMeshProUGUI _actionLabel;

        AuctionService.Lot _lot;
        bool _isMine;
        DateTime _endsAtUtc;
        bool _ended;
        int _lastShownSec = int.MinValue;   // чтобы не аллоцировать строку таймера каждый кадр
        Action<AuctionService.Lot> _onBid;
        Action<AuctionService.Lot> _onCancel;

        public void SetData(AuctionService.Lot lot, CardModel model, bool isMine,
            Action<AuctionService.Lot> onBid, Action<AuctionService.Lot> onCancel)
        {
            _lot = lot;
            _isMine = isMine;
            _onBid = onBid;
            _onCancel = onCancel;
            _endsAtUtc = ParseUtc(lot.EndsAtUtc);
            _ended = lot.Ended;

            if (_cardView != null && model != null) _cardView.SetModel(model);   // полная карта рисуется отсюда
            if (_bidCurrencyIcon != null)
            {
                var icon = MetaIcon.Currency(lot.Currency);
                _bidCurrencyIcon.sprite = icon;
                _bidCurrencyIcon.enabled = icon != null;
            }

            UpdateView();
            TickTimer();
        }

        public override void UpdateView()
        {
            if (_lot == null) return;

            bool hasBids = _lot.CurrentBid > 0;
            if (_bidText != null) _bidText.text = (hasBids ? _lot.CurrentBid : _lot.MinBid).ToString();
            if (_bidLabel != null) _bidLabel.text = hasBids ? UIStrings.AuctionBid : UIStrings.AuctionMinBid;
            if (_sellerText != null) _sellerText.text = _lot.SellerName;

            if (_statusText != null)
            {
                if (_isMine) _statusText.text = hasBids ? UIStrings.AuctionBids(_lot.BidCount) : UIStrings.AuctionNoBids;
                else if (_lot.MyBid > 0) _statusText.text = _lot.MyBid >= _lot.CurrentBid ? UIStrings.AuctionLeading : UIStrings.AuctionOutbid;
                else _statusText.text = hasBids ? UIStrings.AuctionBids(_lot.BidCount) : UIStrings.AuctionNoBids;
            }

            // Кнопка действия
            bool interactable;
            string label;
            if (_ended) { label = UIStrings.AuctionEnded; interactable = false; }
            else if (_isMine) { label = UIStrings.Remove; interactable = !hasBids; }   // снять можно только без ставок
            else { label = _lot.MyBid > 0 ? UIStrings.AuctionRaise : UIStrings.AuctionBid; interactable = true; }

            if (_actionLabel != null) _actionLabel.text = label;
            if (_btnClick != null) _btnClick.interactable = interactable;
        }

        void Update()
        {
            if (_lot == null || _ended) return;
            TickTimer();
        }

        void TickTimer()
        {
            var left = _endsAtUtc - DateTime.UtcNow;
            int sec = (int)left.TotalSeconds;
            if (sec != _lastShownSec)
            {
                _lastShownSec = sec;
                if (_timerText != null) _timerText.text = UIStrings.AuctionTimeLeft(left);
            }
            if (sec <= 0 && !_ended)
            {
                _ended = true;
                if (_lot != null) _lot.Ended = true;
                UpdateView();   // перерисовать кнопку в «Завершён»
            }
        }

        public override void OnClick()
        {
            if (_lot == null || _ended) return;
            if (_isMine) { if (_lot.CurrentBid <= 0) _onCancel?.Invoke(_lot); }
            else _onBid?.Invoke(_lot);
        }

        public override void OnUse() { }

        public override void Unject()
        {
            _onBid = null;
            _onCancel = null;
        }

        public override void Dispose()
        {
            Unject();
            base.Dispose();
        }

        static DateTime ParseUtc(string iso)
        {
            if (DateTime.TryParse(iso, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt))
                return dt;
            return DateTime.UtcNow;
        }
    }
}
