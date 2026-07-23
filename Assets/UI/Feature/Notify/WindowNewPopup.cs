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
    /// Входное окно «что нового» — показывает очередь InboxEntry (награды/новости, выданные удалённо),
    /// по одной, кнопка ОК ведёт к следующей. Дёргается при входе в игру (см. InboxService.GetInbox).
    ///
    /// Префаб: _root (корень), _titleText, _messageText, _currencyText (сводка валют), _cardsRoot +
    /// _cardSlotPrefab (RewardCardSlot), _okButton, _cardConfig.
    /// </summary>
    public class WindowNewPopup : MonoBehaviour
    {
        [SerializeField] private CardConfig _cardConfig;
        [SerializeField] private GameObject _root;
        [SerializeField] private TextMeshProUGUI _titleText;
        [SerializeField] private TextMeshProUGUI _messageText;
        [SerializeField] private TextMeshProUGUI _currencyText;
        [SerializeField] private Transform _cardsRoot;
        [SerializeField] private RewardCardSlot _cardSlotPrefab;
        [SerializeField] private Button _okButton;

        readonly Queue<InboxEntry> _queue = new Queue<InboxEntry>();
        readonly List<RewardCardSlot> _spawned = new List<RewardCardSlot>();

        void Awake()
        {
            if (_okButton != null) _okButton.onClick.AddListener(Next);
            if (_root != null) _root.SetActive(false);
        }

        void OnDestroy()
        {
            if (_okButton != null) _okButton.onClick.RemoveListener(Next);
        }

        /// <summary>Показать очередь входящих (ничего нет → окно не откроется).</summary>
        public void ShowQueue(IEnumerable<InboxEntry> entries)
        {
            _queue.Clear();
            if (entries != null)
                foreach (var e in entries)
                    if (e != null) _queue.Enqueue(e);
            Next();
        }

        void Next()
        {
            ClearCards();
            if (_queue.Count == 0)
            {
                if (_root != null) _root.SetActive(false);
                return;
            }
            Render(_queue.Dequeue());
            if (_root != null) _root.SetActive(true);
        }

        void Render(InboxEntry entry)
        {
            if (_titleText != null) _titleText.text = entry.Title;
            if (_messageText != null) _messageText.text = entry.Message;

            var reward = entry.Reward;
            if (_currencyText != null)
            {
                var sb = new StringBuilder();
                if (reward?.Currencies != null)
                    foreach (var c in reward.Currencies)
                        sb.Append(sb.Length > 0 ? "   " : "").Append('+').Append(c.Amount).Append(' ').Append(UIStrings.CurrencyName(c.Code));
                _currencyText.text = sb.ToString();
                _currencyText.gameObject.SetActive(sb.Length > 0);
            }

            if (_cardsRoot != null && _cardSlotPrefab != null && reward?.Cards != null)
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

        void ClearCards()
        {
            foreach (var s in _spawned)
                if (s != null) Destroy(s.gameObject);
            _spawned.Clear();
        }
    }
}
