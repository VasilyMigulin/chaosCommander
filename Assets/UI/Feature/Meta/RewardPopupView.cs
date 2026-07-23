using System.Collections.Generic;
using System.Text;
using Game.Core.Backend;
using Game.Core.Configs;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// Переиспользуемый оверлей «вы получили»: показывает RewardBundle (валюты текстом + карты
    /// слотами RewardCardSlot). Дёргается любой мета-панелью после клейма/покупки/открытия бустера.
    /// Не панель фреймворка — самодостаточный оверлей (видимость через свой GameObject).
    ///
    /// Префаб: _root (корень оверлея, вкл/выкл), _titleText, _currencyText (сводка валют),
    /// _cardsRoot (контейнер сетки), _cardSlotPrefab (RewardCardSlot), _okButton, _cardConfig.
    /// </summary>
    public class RewardPopupView : MonoBehaviour
    {
        [Header("Config")]
        [SerializeField] private CardConfig _cardConfig;

        [Header("Refs")]
        [SerializeField] private GameObject _root;
        [SerializeField] private TextMeshProUGUI _titleText;
        [SerializeField] private TextMeshProUGUI _currencyText;
        [SerializeField] private Transform _cardsRoot;
        [SerializeField] private RewardCardSlot _cardSlotPrefab;
        [SerializeField] private Button _okButton;

        readonly List<RewardCardSlot> _spawned = new();

        void Awake()
        {
            if (_okButton != null) _okButton.onClick.AddListener(Hide);
            if (_root != null) _root.SetActive(false);
        }

        void OnDestroy()
        {
            if (_okButton != null) _okButton.onClick.RemoveListener(Hide);
        }

        public void Show(RewardBundle reward, string title = null)
        {
            Clear();
            if (reward == null) return;

            if (_titleText != null && title != null) _titleText.text = title;

            // Валюты — сводкой.
            if (_currencyText != null)
            {
                var sb = new StringBuilder();
                if (reward.Currencies != null)
                    foreach (var c in reward.Currencies)
                        // Имя валюты, а не код: «+100 Бабосик» / «+5 Безделушки» (см. UIStrings.CurrencyName).
                        sb.Append(sb.Length > 0 ? "   " : "").Append('+').Append(c.Amount).Append(' ').Append(UIStrings.CurrencyName(c.Code));
                _currencyText.text = sb.ToString();
                _currencyText.gameObject.SetActive(sb.Length > 0);
            }

            // Карты — слотами.
            if (_cardsRoot != null && _cardSlotPrefab != null && reward.Cards != null)
            {
                foreach (var card in reward.Cards)
                {
                    var model = MetaCardResolver.ResolveModel(_cardConfig, card.ItemId);
                    var slot = Instantiate(_cardSlotPrefab, _cardsRoot);
                    slot.gameObject.SetActive(true);
                    slot.SetData(model, card.Amount);   // ложится рубашкой вверх…
                    slot.Flip();                         // …и сразу раскрывается (с VFX редкости)
                    _spawned.Add(slot);
                }
            }

            if (_root != null) _root.SetActive(true);
        }

        public void Hide()
        {
            if (_root != null) _root.SetActive(false);
            Clear();
        }

        void Clear()
        {
            foreach (var s in _spawned)
                if (s != null) Destroy(s.gameObject);
            _spawned.Clear();
        }
    }
}
