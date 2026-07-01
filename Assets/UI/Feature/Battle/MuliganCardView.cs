using AwesomeUI.Core.Card;
using AwesomeUI.Core.Slot;
using DG.Tweening;
using Game.Core.Shared;
using UnityEngine;

namespace AwesomeUI.Feature.Battle
{
    /// <summary>
    /// View одной карты в окне мулигана.
    /// Карту можно выбрать для замены — выбранная подсвечивается красной обводкой.
    /// </summary>
    public class MuliganCardView : CardBaseView
    {
        public int  CardEntity { get; private set; }
        public bool IsSelected { get; private set; }

        private System.Action<MuliganCardView> _onToggle;

        // ── Init ─────────────────────────────────────────────────────────────

        public override SourceSlot Init()
        {
            base.Init();
            SetSelected(false);
            gameObject.SetActive(false);
            return this;
        }

        public override void Unject() { }

        // ── Public API ────────────────────────────────────────────────────────

        public void Setup(int cardEntity, CardVisualData? visualData, string fallbackName, System.Action<MuliganCardView> onToggle)
        {
            CardEntity = cardEntity;
            _onToggle  = onToggle;

            if (visualData.HasValue)
                ApplyVisualData(visualData.Value);

            SetSelected(false);
            gameObject.SetActive(true);

            transform.localScale = Vector3.zero;
            transform.DOScale(Vector3.one, 0.25f).SetEase(Ease.OutBack);
        }

        public void Clear()
        {
            CardEntity = -1;
            IsSelected = false;
            gameObject.SetActive(false);
        }

        public override void OnClick()
        {
            IsSelected = !IsSelected;
            SetSelected(IsSelected);
            _onToggle?.Invoke(this);
        }

        public override void UpdateView()
        {
            SetSelected(IsSelected);
        }

        // ── Private ───────────────────────────────────────────────────────────

        private void SetSelected(bool selected)
        {
            IsSelected = selected;
            SetHighlight(CardHighlightEffect.HighlightType.MulliganSelected, selected);
        }
        public void Lock()
        {
            if (!IsSelected)        // ← сохраняем подсветку выбранных
                ResetHighlight();

            _btnClick.interactable = false;
        }
        public void Unlock()
        { 
            _btnClick.interactable = true;
        }

        public override void OnUse()
        {
        }
    }
}
