using AwesomeUI.Core.Card;
using AwesomeUI.Core.Slot;
using Game.Core.Events;
using Game.Core.Shared;
using UnityEngine;

namespace AwesomeUI.Feature.Battle
{
    /// <summary>
    /// Базовый View карты в руке игрока.
    /// Хранит данные карты, отображает полную визуализацию через CardBaseView.
    /// Производные классы — CommanderCardView и SimpleCardView.
    ///
    /// Подсветка через CardHighlightEffect (один шейдерный компонент):
    ///   Selection    — карта удерживается игроком     (синий)
    ///   Affordable   — на карту хватает ресурсов      (зелёный)
    ///   AbilityReady — у карты активировался эффект   (оранжевый)
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public abstract class PlayCardView : CardBaseView
    {
        public int  CardEntity { get; private set; }
        public bool IsOccupied { get; private set; }
        public bool IsSelected { get; private set; }

        protected PlayCardData _data;
        protected CanvasGroup  _canvasGroup;

        private bool _isAffordable;
        private bool _isAbilityReady;

        // ── Init / Dispose ───────────────────────────────────────────────────

        public override SourceSlot Init()
        {
            base.Init();
            _canvasGroup = GetComponent<CanvasGroup>();
            ResetHighlight();
            gameObject.SetActive(false);
            return this;
        }

        public override void OnInject()
        {
            GameEventBus.Subscribe<CardAffordableChangedEvent>(OnAffordableChanged);
            GameEventBus.Subscribe<CardAbilityReadyChangedEvent>(OnAbilityReadyChanged);
        }

        public override void Unject()
        {
            GameEventBus.Unsubscribe<CardAffordableChangedEvent>(OnAffordableChanged);
            GameEventBus.Unsubscribe<CardAbilityReadyChangedEvent>(OnAbilityReadyChanged);
        }

        public override void Dispose()
        {
            Unject();
            base.Dispose();
            ClearCard();
        }

        // ── Card Data ────────────────────────────────────────────────────────

        /// <summary>Заполнить View данными карты и активировать.</summary>
        public virtual void SetCard(PlayCardData data)
        {
            _data      = data;
            CardEntity = data.CardEntity;
            IsOccupied = true;
            IsSelected = false;
            _isAffordable   = false;
            _isAbilityReady = false;

            if (_icon != null && data.Icon != null)
                _icon.sprite = data.Icon;

            gameObject.SetActive(true);
            UpdateView();
        }

        /// <summary>Очистить View и деактивировать для повторного использования.</summary>
        public virtual void ClearCard()
        {
            CardEntity      = -1;
            IsOccupied      = false;
            IsSelected      = false;
            _isAffordable   = false;
            _isAbilityReady = false;
            ResetHighlight();
            gameObject.SetActive(false);
        }

        // ── Highlights ───────────────────────────────────────────────────────

        public override void OnActive()
        {
            IsSelected = true;
            SetHighlight(CardHighlightEffect.HighlightType.Selection, true);
        }

        public void Deselect()
        {
            IsSelected = false;
            SetHighlight(CardHighlightEffect.HighlightType.Selection, false);
        }

        private void OnAffordableChanged(CardAffordableChangedEvent evt)
        {
            if (evt.CardEntity != CardEntity) return;
            _isAffordable = evt.IsAffordable;
            SetHighlight(CardHighlightEffect.HighlightType.Affordable, _isAffordable);
        }

        private void OnAbilityReadyChanged(CardAbilityReadyChangedEvent evt)
        {
            if (evt.CardEntity != CardEntity) return;
            _isAbilityReady = evt.IsReady;
            SetHighlight(CardHighlightEffect.HighlightType.AbilityReady, _isAbilityReady);
        }

        // ── UpdateView ───────────────────────────────────────────────────────

        public override void UpdateView()
        {
            if (_icon != null)
            {
                _icon.sprite  = _data.Icon;
                _icon.enabled = _data.Icon != null;
            }

            ApplyVisualData(_data.Visual);

            SetHighlight(CardHighlightEffect.HighlightType.Affordable,   _isAffordable);
            SetHighlight(CardHighlightEffect.HighlightType.AbilityReady, _isAbilityReady);
            SetHighlight(CardHighlightEffect.HighlightType.Selection,    IsSelected);
        }
    }
}
