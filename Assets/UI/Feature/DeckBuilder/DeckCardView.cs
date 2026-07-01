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
    /// View карты внутри строящейся колоды.
    /// Для командира счётчик скрывается, показывается значок командира.
    /// Клик — убрать карту / командира из колоды.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class DeckCardView : CardBaseView
    {
        [Header("Deck Counter")]
        [SerializeField] TextMeshProUGUI _counterText;       // "x2"

        [Header("Commander")]
        [SerializeField] GameObject _commanderBadge;         // значок-метка командира

        public CardModel Model { get; private set; }
        public event System.Action<DeckCardView> OnRemoveRequested;

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

            bool isCommander = _data.IsCommander;
            if (_counterText    != null) _counterText.gameObject.SetActive(!isCommander);
            if (_commanderBadge != null) _commanderBadge.SetActive(isCommander);

            if (!isCommander && _counterText != null)
                _counterText.text = $"x{_data.DeckCount}";
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
            OnRemoveRequested?.Invoke(this);
        }
    }
}
