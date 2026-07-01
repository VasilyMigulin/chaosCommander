using AwesomeUI.Core.Slot;
using AwesomeUI.Core.Card;
using Game.Core.DeckBuilder;
using Game.Core.Instance.Card;
using Game.Core.Model.Card;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Feature.DeckBuilder
{
    /// <summary>
    /// View карты в библиотеке игрока.
    /// Счётчик показывает доступное количество: сколько копий ещё можно добавить в колоду.
    /// Доступно = OwnedCount - DeckCount.
    /// Когда доступно 0 — оверлей включается, кнопка блокируется.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class LibraryCardView : CardBaseView
    {
        [Header("Library Counter")]
        [SerializeField] TextMeshProUGUI _counterText;       // сколько копий доступно к добавлению

        [Header("Unavailable Overlay")]
        [SerializeField] GameObject _unavailableOverlay;     // затемнение когда доступных копий 0

        public CardModel Model { get; private set; }
        public event System.Action<LibraryCardView> OnAddRequested;

        DeckCardViewData _data;

        public override SourceSlot Init()
        {
            base.Init();
            return this;
        }

        public void SetData(DeckCardViewData data)
        {
            _data = data;
            Model = data.Model;
            UpdateView();
        }

        public override void UpdateView()
        {
            if (_icon != null)
            {
                _icon.sprite  = _data.Icon;
                _icon.enabled = _data.Icon != null;
            }

            ApplyVisualData(_data.Visual);

            int available = _data.OwnedCount - _data.DeckCount;

            if (_counterText != null)
                _counterText.text = available.ToString();

            bool canAdd = available > 0 && _data.DeckCount < _data.MaxCopies;
            if (_unavailableOverlay != null) _unavailableOverlay.SetActive(!canAdd);
            if (_btnClick           != null) _btnClick.interactable = canAdd;
        }

        // Смена языка: пересобираем визуал (имя/описание) из модели и перерисовываем.
        public override void RefreshLocalization()
        {
            if (Model == null) return;
            _data.Visual = CardVisualDataFactory.From(Model, _data.IsCommander);
            UpdateView();
        }

        public override void OnUse() { }
        public override void Unject()   { }

        public override void OnClick()
        {
            OnAddRequested?.Invoke(this);
        }
    }
}
