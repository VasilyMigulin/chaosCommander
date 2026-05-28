using AwesomeUI.Core.Card;
using AwesomeUI.Core.Slot;
using DG.Tweening;
using Game.Core.Shared;
using UnityEngine;

namespace AwesomeUI.Feature.Battle
{
    /// <summary>
    /// View одной карты в окне раскопки (discover/excavate).
    /// Клик по карте — мгновенный выбор (single-select), окно сообщает выбор и закрывается.
    /// </summary>
    public class CardPickupView : CardBaseView
    {
        public int CardEntity { get; private set; }

        private System.Action<CardPickupView> _onPick;

        public override SourceSlot Init()
        {
            base.Init();
            gameObject.SetActive(false);
            return this;
        }

        public override void Unject() { }
        public override void OnActive() { }
        public override void UpdateView() { }

        public void Setup(int cardEntity, CardVisualData? visualData, System.Action<CardPickupView> onPick)
        {
            CardEntity = cardEntity;
            _onPick    = onPick;

            if (visualData.HasValue)
                ApplyVisualData(visualData.Value);

            ResetHighlight();
            gameObject.SetActive(true);

            transform.localScale = Vector3.zero;
            transform.DOScale(Vector3.one, 0.25f).SetEase(Ease.OutBack);
        }

        public void Clear()
        {
            CardEntity = -1;
            _onPick    = null;
            gameObject.SetActive(false);
        }

        public override void OnClick()
        {
            _onPick?.Invoke(this);
        }
    }
}
