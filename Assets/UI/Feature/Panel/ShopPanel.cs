using System.Collections.Generic;
using AwesomeUI.Core.Panel;
using Game.Core.Backend;
using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// Магазин: бустеры расширений + аватары. Витрину и цены отдаёт сервер (ShopService.GetShop),
    /// покупка проводится серверной функцией BuyStoreItem — клиент только отображает и просит.
    ///
    /// Наследует ComingSoonPanel ради кнопки «Назад» (base.OnInject её вешает). Плашку «Скоро»
    /// в префабе можно скрыть/удалить, когда раздел готов.
    ///
    /// Префаб: _entriesRoot (контейнер Grid/Vertical), _entrySlotPrefab (ShopItemSlot),
    /// _rewardPopup (RewardPopupView), _confirmDialog (ConfirmDialogView), _loadingOverlay,
    /// _emptyHint, _feedbackText, иконки-фолбэки _boosterIcon/_avatarIcon (пока в каталоге нет спрайтов).
    /// Баланс валют показывает HUD (ProfilePlaceholder в топ-баре) — отдельный виджет в панели не нужен.
    /// </summary>
    public class ShopPanel : ComingSoonPanel
    {
        [Header("Shop")]
        [SerializeField] private Transform _entriesRoot;
        [SerializeField] private ShopItemSlot _entrySlotPrefab;
        [SerializeField] private RewardPopupView _rewardPopup;
        [SerializeField] private ConfirmDialogView _confirmDialog;

        [Header("Icons (fallback)")]
        [SerializeField] private Sprite _boosterIcon;
        [SerializeField] private Sprite _avatarIcon;

        [Header("State")]
        [SerializeField] private GameObject _loadingOverlay;
        [SerializeField] private GameObject _emptyHint;
        [SerializeField] private TMPro.TextMeshProUGUI _feedbackText;

        readonly List<ShopItemSlot> _slots = new();
        bool _busy;

        public override void OnInject()
        {
            base.OnInject();   // кнопка «Назад»
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
            ShopService.GetShop(
                onSuccess: resp =>
                {
                    SetLoading(false);
                    Populate(resp);
                },
                onError: err =>
                {
                    SetLoading(false);
                    ShowFeedback(UIStrings.BackendReason(err));
                });
        }

        void Populate(ShopService.ShopResponse resp)
        {
            var entries = resp != null ? resp.Entries : null;
            if (_emptyHint != null) _emptyHint.SetActive(entries == null || entries.Count == 0);
            if (entries == null || _entriesRoot == null || _entrySlotPrefab == null) return;

            foreach (var entry in entries)
            {
                var slot = Instantiate(_entrySlotPrefab, _entriesRoot);
                slot.gameObject.SetActive(true);
                slot.Init();
                slot.SetData(entry, IconFor(entry), OnBuy);
                _slots.Add(slot);
            }
        }

        Sprite IconFor(ShopService.ShopEntry entry)
        {
            if (entry == null) return _boosterIcon;
            if (entry.Category == "avatar")
            {
                // Реальная иконка аватара из AvatarConfig (по itemId), фолбэк — общая заглушка _avatarIcon.
                var cfg = Game.Core.Configs.AvatarConfig.Instance;
                var avatar = cfg != null ? cfg.Get(entry.ItemId) : null;
                return avatar != null && avatar.Icon != null ? avatar.Icon : _avatarIcon;
            }
            return _boosterIcon;
        }

        void OnBuy(ShopService.ShopEntry entry)
        {
            if (_busy || entry == null) return;

            if (_confirmDialog == null) { DoBuy(entry, 1); return; }   // диалог не назначен → покупаем сразу

            // Пачкой берём только ПОВТОРЯЕМОЕ. Аватар — штучный экземпляр (второй бессмыслен), его сервер
            // тоже ограничит одной штукой. Потолок — на сколько хватает денег, но не больше MaxBuyAtOnce.
            int max = 1;
            if (entry.Category != "avatar" && !entry.AlreadyOwned && entry.PriceAmount > 0)
                max = Mathf.Clamp(PlayerWallet.Get(entry.PriceCode) / entry.PriceAmount, 1, ShopService.MaxBuyAtOnce);

            if (max > 1)
                _confirmDialog.ShowQuantity(
                    // Количество ВНУТРИ текста: видно даже если счётчик в префабе не привязан.
                    n => UIStrings.BuyConfirmMany(n, entry.PriceAmount * n, entry.PriceCode),
                    max, n => DoBuy(entry, n));
            else
                _confirmDialog.Show(UIStrings.BuyConfirm(entry.PriceAmount, entry.PriceCode), () => DoBuy(entry, 1));
        }

        void DoBuy(ShopService.ShopEntry entry, int count)
        {
            if (_busy || entry == null) return;
            _busy = true;
            ShowFeedback("");
            ShopService.Buy(entry.ItemId, count,
                onSuccess: resp =>
                {
                    _busy = false;
                    if (resp != null && resp.Success)
                    {
                        if (_rewardPopup != null) _rewardPopup.Show(resp.Reward, entry.DisplayName);
                        Refresh();   // обновить (аватар мог стать «куплено»)
                    }
                    else
                    {
                        Debug.LogWarning($"[ShopPanel] покупка ОТКАЗ: '{entry.ItemId}' → {(resp != null ? resp.Reason : "purchase_failed")}");
                        ShowFeedback(UIStrings.BackendReason(resp != null ? resp.Reason : "purchase_failed"));
                    }
                },
                onError: err => { _busy = false; Debug.LogWarning($"[ShopPanel] покупка ERROR: '{entry.ItemId}' → {err}"); ShowFeedback(UIStrings.BackendReason(err)); });
        }

        void SetLoading(bool on) { if (_loadingOverlay != null) _loadingOverlay.SetActive(on); }

        // Сервер отдаёт КОДЫ отказов — переводим их в текст на языке игрока.
        void ShowFeedback(string msg) { if (_feedbackText != null) _feedbackText.text = msg; }

        void ClearList()
        {
            foreach (var s in _slots) { if (s == null) continue; s.Dispose(); Destroy(s.gameObject); }
            _slots.Clear();
        }

        public override void Unject()
        {
            base.Unject();   // снимает back-кнопку
            ClearList();
        }

        public override void OnDipose()
        {
            ClearList();
            base.OnDipose();
        }
    }
}
