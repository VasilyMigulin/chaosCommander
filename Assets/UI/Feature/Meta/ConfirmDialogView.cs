using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// Переиспользуемый диалог подтверждения (сообщение + Подтвердить/Отмена). Любая мета-панель
    /// вызывает Show(текст, onConfirm) перед покупкой/продажей. Самодостаточный оверлей.
    ///
    /// ВТОРОЙ РЕЖИМ — с количеством (ShowQuantity): показывает −/N/+ и отдаёт выбранное число в onConfirm.
    /// Нужен для покупки пачкой (несколько бустеров за раз). Текст пересобирается на каждое изменение N,
    /// потолок задаёт вызывающая сторона (обычно «сколько хватает денег»). Блок количества не назначен в
    /// префабе → диалог работает как обычный, с количеством 1.
    ///
    /// Префаб: _root (корень, вкл/выкл), _messageText, _confirmBtn, _cancelBtn,
    /// опц. _confirmLabel/_cancelLabel (TMP на кнопках — заполняются локализованными строками),
    /// опц. блок количества: _quantityRoot, _quantityText, _minusBtn, _plusBtn.
    /// </summary>
    public class ConfirmDialogView : MonoBehaviour
    {
        [SerializeField] private GameObject _root;
        [SerializeField] private TextMeshProUGUI _messageText;
        [SerializeField] private Button _confirmBtn;
        [SerializeField] private Button _cancelBtn;
        [SerializeField] private TextMeshProUGUI _confirmLabel;
        [SerializeField] private TextMeshProUGUI _cancelLabel;

        [Header("Количество (покупка пачкой) — опционально")]
        [SerializeField] private GameObject _quantityRoot;
        [SerializeField] private TextMeshProUGUI _quantityText;
        [SerializeField] private Button _minusBtn;
        [SerializeField] private Button _plusBtn;
        [Tooltip("«Максимум» — сразу ставит потолок (обычно «на сколько хватает денег»). Опц.")]
        [SerializeField] private Button _maxBtn;
        [Tooltip("Подпись на кнопке «Максимум» (опц.) — код ставит из UIStrings.")]
        [SerializeField] private TextMeshProUGUI _maxLabel;

        Action _onConfirm;
        Action<int> _onConfirmQuantity;
        Func<int, string> _messageFor;
        int _count = 1, _max = 1;

        void Awake()
        {
            if (_confirmBtn != null) _confirmBtn.onClick.AddListener(Confirm);
            if (_cancelBtn != null) _cancelBtn.onClick.AddListener(Hide);
            // Обе ссылки на ОДНУ кнопку → на ней окажутся оба слушателя, и один клик сделает −1 и +1
            // (внешне: количество залипает, вторая кнопка мертва). Ловим сразу, иначе ищется долго.
            if (_minusBtn != null && _minusBtn == _plusBtn)
                Debug.LogError("[ConfirmDialog] _minusBtn и _plusBtn ссылаются на ОДНУ кнопку — привяжи «−» и «+» " +
                               "к разным объектам, иначе счётчик не работает.", this);

            if (_minusBtn != null) _minusBtn.onClick.AddListener(() => Adjust(-1));
            if (_plusBtn != null) _plusBtn.onClick.AddListener(() => Adjust(+1));
            if (_maxBtn != null) _maxBtn.onClick.AddListener(SetMax);
            if (_maxLabel != null) _maxLabel.text = UIStrings.Max;
            if (_confirmLabel != null) _confirmLabel.text = UIStrings.Confirm;
            if (_cancelLabel != null) _cancelLabel.text = UIStrings.Cancel;
            if (_root != null) _root.SetActive(false);
        }

        void OnDestroy()
        {
            if (_confirmBtn != null) _confirmBtn.onClick.RemoveListener(Confirm);
            if (_cancelBtn != null) _cancelBtn.onClick.RemoveListener(Hide);
            if (_maxBtn != null) _maxBtn.onClick.RemoveListener(SetMax);
        }

        public void Show(string message, Action onConfirm)
        {
            _onConfirm = onConfirm;
            _onConfirmQuantity = null;
            _messageFor = null;
            if (_quantityRoot != null) _quantityRoot.SetActive(false);
            if (_messageText != null) _messageText.text = message;
            if (_root != null) _root.SetActive(true);
        }

        /// <summary>
        /// Подтверждение с выбором количества (−/N/+). messageFor(N) пересобирает текст под текущее N —
        /// так в подтверждении видно итоговую цену. max обычно = «на сколько хватает денег».
        /// Блок количества не собран в префабе → работает как обычное подтверждение с N=1.
        /// </summary>
        public void ShowQuantity(Func<int, string> messageFor, int max, Action<int> onConfirm)
        {
            _onConfirm = null;
            _onConfirmQuantity = onConfirm;
            _messageFor = messageFor;
            _max = Mathf.Max(1, max);
            _count = 1;

            if (_quantityRoot != null) _quantityRoot.SetActive(_max > 1);
            RefreshQuantity();
            if (_root != null) _root.SetActive(true);
        }

        void Adjust(int delta)
        {
            _count = Mathf.Clamp(_count + delta, 1, _max);
            RefreshQuantity();
        }

        // «Максимум» — сразу потолок. При больших пачках (до 99) тыкать «+» десятками кликов бессмысленно.
        void SetMax()
        {
            _count = _max;
            RefreshQuantity();
        }

        void RefreshQuantity()
        {
            if (_messageText != null && _messageFor != null) _messageText.text = _messageFor(_count);
            if (_quantityText != null) _quantityText.text = _count.ToString();
            if (_minusBtn != null) _minusBtn.interactable = _count > 1;
            if (_plusBtn != null) _plusBtn.interactable = _count < _max;
            if (_maxBtn != null) _maxBtn.interactable = _count < _max;
        }

        void Confirm()
        {
            var cb = _onConfirm;
            var cbQty = _onConfirmQuantity;
            int count = _count;
            Hide();
            cb?.Invoke();
            cbQty?.Invoke(count);
        }

        public void Hide()
        {
            _onConfirm = null;
            _onConfirmQuantity = null;
            _messageFor = null;
            if (_root != null) _root.SetActive(false);
        }
    }
}
