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

            ApplyVisualData(_data.Visual);   // сбрасывает баннер уровня — вне боя живого прогресса нет (ниже)

            // Карта-с-уровнями (CardModel.Tiers>0, «Королевская пиньята» и т.п.) — вне матча реального тира
            // не существует (он считается от live-статов, TierSource, только в бою — см. CardTierSystem),
            // поэтому в коллекции бейдж всегда «Ур. 1»: не текущий прогресс, а просто пометка «эта карта растёт».
            if (Model != null && Model.Tiers != null && Model.Tiers.Count > 0) SetTierBanner(1);

            int available = _data.OwnedCount - _data.DeckCount;

            if (_counterText != null)
                _counterText.text = $"X{available}";

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
            if (ConsumeHoldClick()) return;   // это было удержание-предпросмотр, не добавляем
            OnAddRequested?.Invoke(this);
        }

        protected override void OnHoldTriggered()
        {
            if (Model != null) CardInspectBus.RequestDustable(Model);   // из коллекции → с «Порвать»
        }
    }
}
