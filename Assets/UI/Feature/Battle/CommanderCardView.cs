using AwesomeUI.Core.Card;
using AwesomeUI.Core.Slot;
using Game.Core.Events;
using TMPro;
using UnityEngine;

namespace AwesomeUI.Feature.Battle
{
    /// <summary>
    /// View командира. Всегда находится первым (левым) в CardLayout.
    /// Не убирается из руки, но может уходить на перезарядку.
    /// </summary>
    public class CommanderCardView : PlayCardView
    {
        [Header("Commander")]
        [SerializeField] private GameObject    _cooldownOverlay;
        [SerializeField] private TextMeshProUGUI _cooldownText;

        private bool _onCooldown;

        public override SourceSlot Init()
        {
            base.Init();
            SetCooldownVisible(false);
            return this;
        }

        public override void OnInject()
        {
            base.OnInject();
            GameEventBus.Subscribe<CommanderOnCooldownUIEvent>(OnCooldown);
            GameEventBus.Subscribe<CommanderCooldownExpiredUIEvent>(OnCooldownExpired);
        }

        public override void Unject()
        {
            base.Unject();
            GameEventBus.Unsubscribe<CommanderOnCooldownUIEvent>(OnCooldown);
            GameEventBus.Unsubscribe<CommanderCooldownExpiredUIEvent>(OnCooldownExpired);
        }

        public override void OnClick() { }

        public override void UpdateView()
        {
            base.UpdateView();
            SetCooldownVisible(_onCooldown);
        }

        private void OnCooldown(CommanderOnCooldownUIEvent evt)
        {
            if (evt.CardEntity != CardEntity) return;
            _onCooldown = true;
            if (_cooldownText != null)
                _cooldownText.text = evt.CooldownTurns.ToString();
            UpdateView();
        }

        private void OnCooldownExpired(CommanderCooldownExpiredUIEvent evt)
        {
            if (evt.CardEntity != CardEntity) return;
            _onCooldown = false;
            UpdateView();
        }

        private void SetCooldownVisible(bool visible)
        {
            if (_cooldownOverlay != null)
                _cooldownOverlay.SetActive(visible);
        }

        public override void Dispose()
        {
            Unject();
            base.Dispose();
        }
    }
}
