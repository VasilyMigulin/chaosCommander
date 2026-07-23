using System.Collections.Generic;
using AwesomeUI.Core.Panel;
using Game.Core.Backend;
using Game.Core.Configs;
using Game.Core.DeckBuilder;
using Game.Core.Model.Card;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// Аукцион СО СТАВКАМИ. Две вкладки — «Обзор» (все лоты, ставить) и «Мои» (свои лоты + продажа своих карт).
    /// Лот живёт 1 час, единая валюта (золото ИЛИ гемы — выбор продавца), победитель по дедлайну. Комиссия —
    /// buyer's premium (см. AuctionService). Лимит лотов = фишка «рынок вот-вот лопнет». Наследует ComingSoonPanel.
    ///
    /// Префаб:
    ///   Вкладки: _browseTabBtn, _myTabBtn → _browseView / _myView.
    ///   Обзор:   _browseRoot (контейнер), _listingSlotPrefab (AuctionListingSlot), _capacityText, _hintText.
    ///   Мои:     под-вкладки _myLotsTabBtn/_sellTabBtn → _myLotsView/_sellView. Внутри — свои Scroll→View→Layout:
    ///            _myListingsRoot (свои лоты) и _sellRoot (карты к продаже), _sellSlotPrefab (AuctionSellSlot).
    ///   Продажа: встроена в AuctionSellSlot (цена/валюта/«Выставить» прямо в карточке — без диалога).
    ///   Ставка:  _bidDialog, _bidCardNameText, _bidCurrentText, _bidInput (TMP_InputField), _bidCostText,
    ///            _confirmBidBtn, _cancelBidBtn.
    ///   Прочее:  _cardConfig, _loadingOverlay, _feedbackText.
    /// </summary>
    public class AuctionPanel : ComingSoonPanel
    {
        [Header("Config")]
        [SerializeField] private CardConfig _cardConfig;

        [Header("Tabs")]
        [SerializeField] private Button _browseTabBtn;
        [SerializeField] private Button _myTabBtn;
        [SerializeField] private GameObject _browseView;
        [SerializeField] private GameObject _myView;

        [Header("Browse")]
        [SerializeField] private Transform _browseRoot;
        [SerializeField] private AuctionListingSlot _listingSlotPrefab;
        [SerializeField] private TextMeshProUGUI _capacityText;
        [SerializeField] private TextMeshProUGUI _hintText;

        [Header("My / Sell")]
        [SerializeField] private Transform _myListingsRoot;
        [SerializeField] private Transform _sellRoot;
        [SerializeField] private AuctionSellSlot _sellSlotPrefab;

        [Header("My sub-tabs (Мои лоты / Продать)")]
        [Tooltip("Кнопка под-вкладки «Мои лоты» (активные лоты).")]
        [SerializeField] private Button _myLotsTabBtn;
        [Tooltip("Кнопка под-вкладки «Продать» (карты к выставлению).")]
        [SerializeField] private Button _sellTabBtn;
        [Tooltip("Контейнер «Мои лоты» (внутри — свой Scroll→View→Layout = My Listings Root).")]
        [SerializeField] private GameObject _myLotsView;
        [Tooltip("Контейнер «Продать» (внутри — свой Scroll→View→Layout = Sell Root).")]
        [SerializeField] private GameObject _sellView;

        [Header("Bid dialog")]
        [SerializeField] private GameObject _bidDialog;
        [SerializeField] private TextMeshProUGUI _bidCardNameText;
        [SerializeField] private TextMeshProUGUI _bidCurrentText;
        [SerializeField] private TMP_InputField _bidInput;
        [SerializeField] private TextMeshProUGUI _bidCostText;
        [SerializeField] private Button _confirmBidBtn;
        [SerializeField] private Button _cancelBidBtn;

        [Header("Shared")]
        [SerializeField] private GameObject _loadingOverlay;
        [SerializeField] private TextMeshProUGUI _feedbackText;

        readonly List<AuctionListingSlot> _browseSlots = new();
        readonly List<AuctionListingSlot> _mySlots = new();
        readonly List<AuctionSellSlot> _sellSlots = new();

        bool _busy;
        int _feePercent = 10;
        AuctionService.Lot _pendingBidLot;
        AuctionService.AuctionState _state;

        public override void OnInject()
        {
            base.OnInject();

            if (_browseTabBtn != null) _browseTabBtn.onClick.AddListener(ShowBrowse);
            if (_myTabBtn != null) _myTabBtn.onClick.AddListener(ShowMy);
            if (_myLotsTabBtn != null) _myLotsTabBtn.onClick.AddListener(ShowMyLots);
            if (_sellTabBtn != null) _sellTabBtn.onClick.AddListener(ShowSell);
            if (_confirmBidBtn != null) _confirmBidBtn.onClick.AddListener(ConfirmBid);
            if (_cancelBidBtn != null) _cancelBidBtn.onClick.AddListener(CloseBidDialog);
            if (_bidInput != null) _bidInput.onValueChanged.AddListener(_ => UpdateBidCost());

            PlayerLibrary.Changed += OnLibraryChanged;   // ресинк после расчёта лотов вернул карту → обновить «Продать»
            CloseBidDialog();
        }

        // Библиотека изменилась (расчёт лотов вернул/выдал карту, списание) — если открыта «Мои», обновить.
        void OnLibraryChanged()
        {
            if (_myView != null && _myView.activeInHierarchy) RefreshMy();
        }

        public override void OnOpen(params System.Action[] onComplete)
        {
            base.OnOpen(onComplete);
            // При входе сперва рассчитываем истёкшие лоты (пока нет Scheduled Task — так лоты оседают,
            // когда кто-то заходит на рынок), затем показываем обзор. Двойной расчёт при одновременном
            // входе двух игроков считаем приемлемым для MVP (малый онлайн); крон/внешняя БД — при взлёте.
            AuctionService.ResolveNow(_ => ShowBrowse(), _ => ShowBrowse());
        }

        // ── Вкладки ────────────────────────────────────────────────────────────

        void ShowBrowse()
        {
            if (_browseView != null) _browseView.SetActive(true);
            if (_myView != null) _myView.SetActive(false);
            RefreshBrowse();
        }

        void ShowMy()
        {
            if (_browseView != null) _browseView.SetActive(false);
            if (_myView != null) _myView.SetActive(true);
            SetMySubView(myLots: true);   // по умолчанию под-вкладка «Мои лоты»
            RefreshMy();                   // грузим ОБА списка сразу (под-вкладки только переключают видимость)
        }

        // Под-вкладки внутри «Мои»: только переключают видимость (данные уже загружены RefreshMy).
        void ShowMyLots() => SetMySubView(true);
        void ShowSell()   => SetMySubView(false);

        void SetMySubView(bool myLots)
        {
            if (_myLotsView != null) _myLotsView.SetActive(myLots);
            if (_sellView != null) _sellView.SetActive(!myLots);
        }

        // ── Обзор ──────────────────────────────────────────────────────────────

        void RefreshBrowse()
        {
            ClearBrowse();
            SetLoading(true);
            AuctionService.GetListings(
                onSuccess: state =>
                {
                    SetLoading(false);
                    _state = state;
                    if (state != null) _feePercent = state.FeePercent;
                    UpdateCapacity();
                    if (state == null || state.Lots == null || _browseRoot == null || _listingSlotPrefab == null) return;
                    foreach (var lot in state.Lots)
                        _browseSlots.Add(SpawnLot(_browseRoot, lot));
                },
                onError: err => { SetLoading(false); ShowFeedback(err); });
        }

        void UpdateCapacity()
        {
            if (_state == null) return;
            if (_capacityText != null) _capacityText.text = UIStrings.AuctionCapacity(_state.LotCount, _state.MaxLots);
            if (_hintText != null)
            {
                if (_state.IsFull) _hintText.text = UIStrings.AuctionFull;
                else if (_state.MaxLots > 0 && _state.LotCount >= _state.MaxLots * 0.8f) _hintText.text = UIStrings.AuctionNearFull;
                else _hintText.text = "";
            }
        }

        // ── Мои лоты + продажа ───────────────────────────────────────────────────

        void RefreshMy()
        {
            ClearMy();
            SetLoading(true);
            AuctionService.GetListings(
                onSuccess: state =>
                {
                    SetLoading(false);
                    _state = state;
                    if (state != null) _feePercent = state.FeePercent;
                    string myId = PlayFabService.PlayFabId;
                    if (state != null && state.Lots != null && _myListingsRoot != null && _listingSlotPrefab != null)
                        foreach (var lot in state.Lots)
                            if (lot.SellerId == myId)
                                _mySlots.Add(SpawnLot(_myListingsRoot, lot));
                },
                onError: err => { SetLoading(false); ShowFeedback(err); });

            BuildSellList();
        }

        void BuildSellList()
        {
            if (_sellRoot == null || _sellSlotPrefab == null) return;
            foreach (var kv in PlayerLibrary.Entries)
            {
                var model = kv.Value.Model;   // PlayerLibrary отдаёт CardModel напрямую — StaticCardView рисует его сам
                string itemId = CardItemId.Of(model.ExpansionId, model.Id);
                var slot = Instantiate(_sellSlotPrefab, _sellRoot);
                slot.gameObject.SetActive(true);
                slot.Init();
                slot.SetData(itemId, model, kv.Value.OwnedCount, OnListRequested);   // цена/валюта/«Выставить» — в самой карточке
                _sellSlots.Add(slot);
            }
        }

        AuctionListingSlot SpawnLot(Transform root, AuctionService.Lot lot)
        {
            bool mine = lot.SellerId == PlayFabService.PlayFabId;
            var slot = Instantiate(_listingSlotPrefab, root);
            slot.gameObject.SetActive(true);
            slot.Init();
            slot.SetData(lot, ResolveModel(lot), mine, OnBid, OnCancel);   // полную карту рисует StaticCardView в слоте
            return slot;
        }

        // lot.ItemId → CardModel (для полной карты в слоте). null → карта не найдена в конфиге.
        CardModel ResolveModel(AuctionService.Lot lot)
        {
            if (lot == null || _cardConfig == null || !lot.TryResolve(out var exp, out var id)) return null;
            var inst = _cardConfig.Get(exp, id);
            return inst != null ? inst.CardData : null;
        }

        // ── Ставка ───────────────────────────────────────────────────────────────

        void OnBid(AuctionService.Lot lot)
        {
            if (_busy || lot == null) return;
            _pendingBidLot = lot;
            if (_bidCardNameText != null)
            {
                var visual = MetaCardResolver.Resolve(_cardConfig, lot.ItemId);
                _bidCardNameText.text = visual.Found ? visual.Name : lot.ItemId;
            }
            int current = lot.CurrentBid > 0 ? lot.CurrentBid : lot.MinBid;
            if (_bidCurrentText != null) _bidCurrentText.text = $"{current} {UIStrings.CurrencyName(lot.Currency)}";
            // Подсказываем минимально допустимую ставку (строго выше текущей, либо стартовую если ставок нет).
            int suggested = lot.CurrentBid > 0 ? lot.CurrentBid + 1 : lot.MinBid;
            if (_bidInput != null) _bidInput.text = suggested.ToString();
            UpdateBidCost();
            if (_bidDialog != null) _bidDialog.SetActive(true);
        }

        void UpdateBidCost()
        {
            if (_bidCostText == null || _pendingBidLot == null) return;
            int amount = ParseInt(_bidInput != null ? _bidInput.text : "0");
            int total = AuctionService.Lot.WithFee(amount, _feePercent);
            _bidCostText.text = UIStrings.AuctionBidCost(total, _pendingBidLot.Currency);
        }

        void ConfirmBid()
        {
            if (_busy || _pendingBidLot == null) return;
            int amount = ParseInt(_bidInput != null ? _bidInput.text : "0");
            if (amount <= 0) { ShowFeedback(UIStrings.EnterPrice); return; }

            var lot = _pendingBidLot;
            _busy = true;
            AuctionService.PlaceBid(lot.LotId, amount,
                onSuccess: resp =>
                {
                    _busy = false;
                    if (resp != null && resp.Success) { CloseBidDialog(); RefreshBrowse(); }
                    else ShowFeedback(UIStrings.BackendReason(resp != null ? resp.Reason : "bid_failed"));
                },
                onError: err => { _busy = false; ShowFeedback(err); });
        }

        void CloseBidDialog()
        {
            _pendingBidLot = null;
            if (_bidDialog != null) _bidDialog.SetActive(false);
        }

        // ── Снять свой лот ────────────────────────────────────────────────────────

        void OnCancel(AuctionService.Lot lot)
        {
            if (_busy || lot == null) return;
            _busy = true;
            AuctionService.CancelListing(lot.LotId,
                onSuccess: resp =>
                {
                    _busy = false;
                    if (resp != null && resp.Success)
                    {
                        // сервер вернул карту в инвентарь → отразить в локальной библиотеке (иначе видна только после рестарта)
                        var model = ResolveModel(lot);
                        if (model != null) PlayerLibrary.AddCard(model, 1);
                        RefreshMy();
                    }
                    else ShowFeedback(UIStrings.BackendReason(resp != null ? resp.Reason : "cancel failed"));
                },
                onError: err => { _busy = false; ShowFeedback(err); });
        }

        // ── Выставить свою карту (инлайн из AuctionSellSlot: itemId + валюта + цена) ────────

        void OnListRequested(string itemId, string currency, int price)
        {
            if (_busy || string.IsNullOrEmpty(itemId) || price <= 0) return;
            if (_state != null && _state.IsFull) { ShowFeedback(UIStrings.AuctionFull); return; }

            _busy = true;
            AuctionService.ListCard(itemId, currency, price,
                onSuccess: resp =>
                {
                    _busy = false;
                    if (resp != null && resp.Success)
                    {
                        // карта ушла в escrow → убрать из локальной библиотеки, чтобы пропала из сетки продажи
                        if (CardItemId.TryParse(itemId, out var exp, out var id))
                        {
                            PlayerLibrary.RemoveCard(exp, id, 1);
                            DeckStorage.PruneAgainstLibrary();   // колоды не должны держать выставленные карты
                        }
                        RefreshMy();
                    }
                    else ShowFeedback(UIStrings.BackendReason(resp != null ? resp.Reason : "list failed"));
                },
                onError: err => { _busy = false; ShowFeedback(err); });
        }

        // ── Утилиты ──────────────────────────────────────────────────────────────

        static int ParseInt(string s) { int.TryParse(s, out int v); return v; }
        void SetLoading(bool on) { if (_loadingOverlay != null) _loadingOverlay.SetActive(on); }
        void ShowFeedback(string msg) { if (_feedbackText != null) _feedbackText.text = msg; }

        void ClearBrowse() { ClearSlots(_browseSlots); }
        void ClearMy() { ClearSlots(_mySlots); ClearSellSlots(); }

        void ClearSlots(List<AuctionListingSlot> list)
        {
            foreach (var s in list) { if (s == null) continue; s.Dispose(); Destroy(s.gameObject); }
            list.Clear();
        }

        void ClearSellSlots()
        {
            foreach (var s in _sellSlots) { if (s == null) continue; s.Dispose(); Destroy(s.gameObject); }
            _sellSlots.Clear();
        }

        public override void Unject()
        {
            base.Unject();
            if (_browseTabBtn != null) _browseTabBtn.onClick.RemoveListener(ShowBrowse);
            if (_myTabBtn != null) _myTabBtn.onClick.RemoveListener(ShowMy);
            if (_myLotsTabBtn != null) _myLotsTabBtn.onClick.RemoveListener(ShowMyLots);
            if (_sellTabBtn != null) _sellTabBtn.onClick.RemoveListener(ShowSell);
            if (_confirmBidBtn != null) _confirmBidBtn.onClick.RemoveListener(ConfirmBid);
            if (_cancelBidBtn != null) _cancelBidBtn.onClick.RemoveListener(CloseBidDialog);
            PlayerLibrary.Changed -= OnLibraryChanged;
            ClearBrowse();
            ClearMy();
        }

        public override void OnDipose()
        {
            ClearBrowse();
            ClearMy();
            base.OnDipose();
        }
    }
}
