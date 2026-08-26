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

        [Header("Sideboard")]
        [Tooltip("Кнопка «редактировать отложенные» — загорается только у карты, которая объявляет свою " +
                 "зону (Сказочник). Нажатие переводит список колоды в режим правки этой зоны.")]
        [SerializeField] Button _editSideboardButton;

        public CardModel Model { get; private set; }
        public event System.Action<DeckCardView> OnRemoveRequested;

        /// <summary>Нажата кнопка «редактировать отложенные».</summary>
        public event System.Action<DeckCardView> OnEditSideboardRequested;

        DeckCardViewData _data;

        public override SourceSlot Init()
        {
            base.Init();
            if (_editSideboardButton != null)
            {
                // Своя кнопка, а не общий клик по карте: клик по карте уже занят удалением из колоды,
                // и вешать на него второе значение значило бы ломать привычное поведение остальных карт.
                _editSideboardButton.onClick.RemoveListener(RaiseEditSideboard);
                _editSideboardButton.onClick.AddListener(RaiseEditSideboard);
            }
            return this;
        }

        void RaiseEditSideboard() => OnEditSideboardRequested?.Invoke(this);

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

            // Карта-с-уровнями (CardModel.Tiers>0) — та же пометка «Ур. 1», что и в LibraryCardView (см. её
            // коммент): вне матча реального тира не существует, бейдж тут просто «эта карта растёт».
            if (Model != null && Model.Tiers != null && Model.Tiers.Count > 0) SetTierBanner(1);

            bool isCommander = _data.IsCommander;
            if (_counterText    != null) _counterText.gameObject.SetActive(!isCommander);
            if (_commanderBadge != null) _commanderBadge.SetActive(isCommander);

            if (!isCommander && _counterText != null)
                _counterText.text = $"X{_data.DeckCount}";

            if (_editSideboardButton != null)
                _editSideboardButton.gameObject.SetActive(_data.HasSideboard);
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
            if (ConsumeHoldClick()) return;   // это было удержание-предпросмотр, не убираем карту
            OnRemoveRequested?.Invoke(this);
        }

        protected override void OnHoldTriggered()
        {
            if (Model != null) CardInspectBus.RequestDustable(Model);   // из коллекции → с «Порвать»
        }
    }
}
