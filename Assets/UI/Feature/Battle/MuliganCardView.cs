using AwesomeUI.Core.Card;
using AwesomeUI.Core.Slot;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Feature.Battle
{
    /// <summary>
    /// View одной карты в окне мулигана.
    /// Карту можно выбрать для замены — выбранная подсвечивается оверлеем.
    /// </summary>
    public class MuliganCardView : CardBaseView
    {
        [Header("Mulligan")]
        [SerializeField] private GameObject      _selectedOverlay;
        [SerializeField] private TextMeshProUGUI _cardNameText;
        [SerializeField] private Image           _iconImage;

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

        public void Setup(int cardEntity, Sprite icon, string cardName, System.Action<MuliganCardView> onToggle)
        {
            CardEntity = cardEntity;
            _onToggle  = onToggle;

            if (_iconImage    != null && icon != null) _iconImage.sprite = icon;
            if (_cardNameText != null) _cardNameText.text = cardName;

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
            if (_selectedOverlay != null)
                _selectedOverlay.SetActive(selected);
        }

        public override void OnActive()
        { 
        }
    }
}
