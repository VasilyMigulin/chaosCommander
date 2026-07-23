using System;
using System.Collections.Generic;
using System.Globalization;
using AwesomeUI.Core.Panel;
using Game.Core.Backend;
using Game.Core.Configs;
using Game.Core.Model.Card;
using UnityEngine;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// Чёрный рынок: ротируемый курируемый набор карт (4 common / 3 rare / 2 epic / 1 leg / 1 exotic).
    /// Сервер (BlackMarketService.Get) роллит набор на ротацию (среда). Купил ОДНУ карту → остальные
    /// гаснут до следующей ротации. Наследует ComingSoonPanel (кнопка «Назад»).
    ///
    /// Префаб: _cardConfig (резолв CardModel по itemId), _offersRoot (Grid), _offerSlotPrefab (CardOfferSlot —
    /// внутри StaticCardView рисует полную карту), _rewardPopup, _confirmDialog, _timerText (до ротации),
    /// _loadingOverlay, _emptyHint, _feedbackText. Баланс валют показывает HUD (ProfilePlaceholder).
    /// </summary>
    public class BlackMarketPanel : ComingSoonPanel
    {
        [Header("Config")]
        [SerializeField] private CardConfig _cardConfig;

        [Header("Market")]
        [SerializeField] private Transform _offersRoot;
        [SerializeField] private CardOfferSlot _offerSlotPrefab;
        [SerializeField] private RewardPopupView _rewardPopup;
        [SerializeField] private ConfirmDialogView _confirmDialog;

        [Header("State")]
        [SerializeField] private TMPro.TextMeshProUGUI _timerText;
        [SerializeField] private GameObject _loadingOverlay;
        [SerializeField] private GameObject _emptyHint;
        [SerializeField] private TMPro.TextMeshProUGUI _feedbackText;

        readonly List<CardOfferSlot> _slots = new();
        bool _busy;
        bool _hasRotationTime;
        DateTime _nextRotationUtc;

        public override void OnInject()
        {
            base.OnInject();
        }

        public override void OnOpen(params System.Action[] onComplete)
        {
            base.OnOpen(onComplete);
            Refresh();   // грузим при ОТКРЫТИИ (после логина), а не на init
        }

        void Refresh()
        {
            ClearList();
            SetLoading(true);
            BlackMarketService.Get(
                onSuccess: resp =>
                {
                    SetLoading(false);
                    _hasRotationTime = DateTime.TryParse(resp != null ? resp.NextRotationUtc : null,
                        CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                        out _nextRotationUtc);
                    Populate(resp);
                },
                onError: err =>
                {
                    SetLoading(false);
                    ShowFeedback(err);
                });
        }

        void Populate(BlackMarketService.BlackMarketResponse resp)
        {
            ClearList();   // подстраховка: если два Refresh наложились, каждый ответ чистит перед заполнением → без дублей
            var offers = resp != null ? resp.Offers : null;
            if (_emptyHint != null) _emptyHint.SetActive(offers == null || offers.Count == 0);
            if (offers == null || _offersRoot == null || _offerSlotPrefab == null) return;

            foreach (var offer in offers)
            {
                var slot = Instantiate(_offerSlotPrefab, _offersRoot);
                slot.gameObject.SetActive(true);
                slot.Init();
                slot.SetData(offer, ResolveModel(offer), OnBuy);   // полную карту рисует StaticCardView в слоте
                _slots.Add(slot);
            }
        }

        // offer.ItemId → CardModel (для полной карты в слоте). null → карта не найдена в конфиге.
        CardModel ResolveModel(BlackMarketService.Offer offer)
        {
            if (offer == null || _cardConfig == null || !offer.TryResolve(out var exp, out var id)) return null;
            var inst = _cardConfig.Get(exp, id);
            return inst != null ? inst.CardData : null;
        }

        void OnBuy(BlackMarketService.Offer offer)
        {
            if (_busy || offer == null) return;
            if (_confirmDialog != null)
                _confirmDialog.Show(UIStrings.BuyConfirm(offer.PriceAmount, offer.PriceCode), () => DoBuy(offer));
            else
                DoBuy(offer);
        }

        void DoBuy(BlackMarketService.Offer offer)
        {
            if (_busy || offer == null) return;
            _busy = true;
            ShowFeedback("");
            var model = ResolveModel(offer);
            string title = model != null ? model.Name : UIStrings.Purchase;
            BlackMarketService.Buy(offer.ItemId,
                onSuccess: resp =>
                {
                    _busy = false;
                    if (resp != null && resp.Success)
                    {
                        if (_rewardPopup != null) _rewardPopup.Show(resp.Reward, title);
                        Refresh();   // остальные офферы гаснут до ротации
                    }
                    else ShowFeedback(resp != null ? resp.Reason : "purchase failed");
                },
                onError: err => { _busy = false; ShowFeedback(err); });
        }

        void Update()
        {
            if (!_hasRotationTime || _timerText == null) return;
            var left = ServerClock.TimeUntil(_nextRotationUtc);
            _timerText.text = left.Days > 0
                ? $"{left.Days}d {left.Hours:00}:{left.Minutes:00}:{left.Seconds:00}"
                : $"{left.Hours:00}:{left.Minutes:00}:{left.Seconds:00}";
        }

        void SetLoading(bool on) { if (_loadingOverlay != null) _loadingOverlay.SetActive(on); }
        void ShowFeedback(string msg) { if (_feedbackText != null) _feedbackText.text = msg; }

        void ClearList()
        {
            foreach (var s in _slots) { if (s == null) continue; s.Dispose(); Destroy(s.gameObject); }
            _slots.Clear();
        }

        public override void Unject()
        {
            base.Unject();
            ClearList();
        }

        public override void OnDipose()
        {
            ClearList();
            base.OnDipose();
        }
    }
}
