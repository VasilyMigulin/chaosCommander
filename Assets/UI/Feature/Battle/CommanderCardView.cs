using AwesomeUI.Core.Attributes;
using AwesomeUI.Core.Card;
using AwesomeUI.Core.Slot;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Shared;
using Leopotam.EcsLite;
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
        [UIInject] EcsWorld _world;

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

        // Источник правды по КД — сама ECS-сущность, а не только накопленная событийная история.
        // _onCooldown иначе мог остаться от ПРЕДЫДУЩЕГО матча (тот же пул-инстанс вьюхи переиспользуется
        // между заходами: базовый ClearCard сбрасывает _isAffordable/_isAbilityReady, но не знает про
        // командирское поле, а сам слот возврат в руку после смерти проводит через SetCard напрямую, минуя
        // ClearCard) — у свежего командира КД не бывает (RunCommanderCooldownSystem: первое наблюдение не
        // триггерит), но событие CommanderCooldownExpiredUIEvent для него тоже никогда не придёт (снимать
        // нечего — компонента не было), так что дождаться самокоррекции неоткуда. Гонка с «живым» КД
        // (событие уже пришло чуть раньше SetCard при возврате командира в руку) не страшна: если реальный
        // компонент уже есть на сущности — прочитаем true отсюда же; если добавится мгновением позже —
        // достроит тот же CommanderOnCooldownUIEvent, как и раньше.
        public override void SetCard(PlayCardData data)
        {
            base.SetCard(data);
            _onCooldown = false;
            if (_world != null)
            {
                var cdPool = _world.GetPool<CommanderCooldownComponent>();
                if (cdPool.Has(CardEntity))
                {
                    _onCooldown = true;
                    if (_cooldownText != null)
                        _cooldownText.text = cdPool.Get(CardEntity).TurnsRemaining.ToString();
                }
            }
            UpdateView();
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
