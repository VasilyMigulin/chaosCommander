using System;
using AwesomeUI.Core.Slot;
using Game.Core.Backend;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// Слот товара магазина (бустер/аватар). Панель спавнит по слоту на ShopEntry.
    /// Аватар, который уже есть, показывает бейдж «куплено» и не кликается.
    ///
    /// Префаб: Button на корне (IsButton=true, EnableIcon=false), _iconImage, _nameText,
    /// _priceText (число цены), _priceIcon (иконка валюты — из InterfaceConfig), _ownedBadge (для durable).
    /// _ownedBadge — просто контейнер: показывается при AlreadyOwned, а «Куплено» кладётся ВНУТРЬ него
    /// отдельным LocalizedText (ключ ui.shop.owned) — код текст не трогает.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class ShopItemSlot : SourceSlot
    {
        [SerializeField] private Image _iconImage;
        [SerializeField] private TextMeshProUGUI _nameText;
        [SerializeField] private TextMeshProUGUI _priceText;
        [Tooltip("Иконка валюты рядом с ценой — берётся из InterfaceConfig по коду (GD/GM). Если не назначена, цена покажется текстом с кодом.")]
        [SerializeField] private Image _priceIcon;
        [SerializeField] private GameObject _ownedBadge;

        ShopService.ShopEntry _entry;
        Action<ShopService.ShopEntry> _onBuy;
        bool _subscribed;

        public void SetData(ShopService.ShopEntry entry, Sprite icon, Action<ShopService.ShopEntry> onBuy)
        {
            _entry = entry;
            _onBuy = onBuy;

            if (_iconImage != null)
            {
                _iconImage.sprite = icon;
                _iconImage.enabled = icon != null;
            }
            if (!_subscribed) { PlayerWallet.OnChanged += UpdateView; _subscribed = true; }
            UpdateView();
        }

        public override void UpdateView()
        {
            if (_entry == null) return;
            // DisplayName из Title Data может быть КЛЮЧОМ локализации (ui.shop.booster_standard) или
            // сырым текстом — GetText вернёт перевод, а на неизвестном ключе сам текст. Работают оба.
            if (_nameText != null)
                _nameText.text = Game.Core.Shared.CardTextLocalization.GetText(_entry.DisplayName, _entry.DisplayName);

            // Цена: если есть иконка валюты (InterfaceConfig) — показываем число + иконку; иначе фолбэк
            // «число КОД» (GD/GM), чтобы цена не пропала, даже если конфиг/иконка не назначены.
            var currencyIcon = AwesomeUI.Core.Card.InterfaceConfig.Current != null
                ? AwesomeUI.Core.Card.InterfaceConfig.Current.GetCurrencyIcon(_entry.PriceCode) : null;
            if (_priceIcon != null)
            {
                _priceIcon.sprite = currencyIcon;
                _priceIcon.enabled = currencyIcon != null;
            }
            if (_priceText != null)
                _priceText.text = currencyIcon != null ? _entry.PriceAmount.ToString() : $"{_entry.PriceAmount} {_entry.PriceCode}";

            if (_ownedBadge != null) _ownedBadge.SetActive(_entry.AlreadyOwned);

            bool affordable = PlayerWallet.Get(_entry.PriceCode) >= _entry.PriceAmount;
            if (_btnClick != null) _btnClick.interactable = !_entry.AlreadyOwned && affordable;
        }

        public override void OnClick()
        {
            if (_entry == null || _entry.AlreadyOwned) return;
            if (PlayerWallet.Get(_entry.PriceCode) < _entry.PriceAmount) return;
            _onBuy?.Invoke(_entry);
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
