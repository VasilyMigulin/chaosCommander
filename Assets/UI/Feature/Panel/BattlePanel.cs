using AwesomeUI.Core.Attributes;
using AwesomeUI.Core.Panel;
using AwesomeUI.Interface;
using Game.Core.Events;
using Game.Core.Shared.Interface;
using System.Collections.Generic;
using UnityEngine;

namespace AwesomeUI.Feature.Battle
{
    /// <summary>
    /// Главная панель боя.
    /// Содержит:
    ///   — CardLayout: рука игрока
    ///   — MuliganWindow: окно мулигана
    ///   — OpponentCardPlayView: всплывающая карта оппонента
    ///   — ResourceIndicatorView[]: индикаторы ресурсов своих и оппонента
    ///   — TurnTimerView: таймер хода локального игрока
    ///   — TurnHintView: хинт смены хода
    /// Общение с игровой логикой только через GameEventBus и IBattleUIContext.
    /// </summary>
    public class BattlePanel : SourcePanel
    {
        [UIInject] private IBattleUIContext _battleContext;

        [Header("Hand")]
        [SerializeField] private CardLayout _cardLayout;

        [Header("Mulligan")]
        [SerializeField] private MuliganWindow _muliganWindow;

        [Header("Opponent Card")]
        [SerializeField] private OpponentCardPlayView _opponentCardPlayView;

        [Header("Card Detail")]
        [SerializeField] private CardDetailPopupView _cardDetailPopupView;

        [Header("Active Auras (локальный игрок)")]
        [Tooltip("Бар активных аур ЛОКАЛЬНОГО игрока — тот же класс AuraStatusBarView используется на аватаре "
               + "оппонента, но там данные приходят напрямую (AvatarPlayerView.SetAuras), а сюда — только через "
               + "AuraStatusChangedUIEvent (см. OnAuraStatusChanged). Компонент сам не подписывается на шину —"
               + "иначе оба инстанса ловили бы одно и то же локальное событие.")]
        [SerializeField] private AuraStatusBarView _localAuraBarView;

        [Header("Resources")]
        [SerializeField] private List<ResourceIndicatorView> _resourceIndicators;

        [Header("Turn")]
        [SerializeField] private TurnTimerView _turnTimerView;
        [SerializeField] private TurnHintView  _turnHintView;
        [SerializeField] private EndTurnButtonView _endTurnButtonView;

        public override void Init(IPanelController panelController)
        {
            base.Init(panelController);

            if (_cardLayout == null)
                _cardLayout = GetComponentInChildren<CardLayout>(true);

            if (_muliganWindow == null)
                _muliganWindow = GetComponentInChildren<MuliganWindow>(true);

            if (_opponentCardPlayView == null)
                _opponentCardPlayView = GetComponentInChildren<OpponentCardPlayView>(true);

            if (_cardDetailPopupView == null)
                _cardDetailPopupView = GetComponentInChildren<CardDetailPopupView>(true);

            if (_localAuraBarView == null)
                _localAuraBarView = GetComponentInChildren<AuraStatusBarView>(true);

            if (_turnTimerView == null)
                _turnTimerView = GetComponentInChildren<TurnTimerView>(true);

            if (_turnHintView == null)
                _turnHintView = GetComponentInChildren<TurnHintView>(true);

            if (_endTurnButtonView == null)
                _endTurnButtonView = GetComponentInChildren<EndTurnButtonView>(true);

            if (_resourceIndicators == null || _resourceIndicators.Count == 0)
            {
                var found = GetComponentsInChildren<ResourceIndicatorView>(true);
                _resourceIndicators = new List<ResourceIndicatorView>(found);
            }

            // CardLayout скрыт до начала матча (открывается после мулигана)
            if (_cardLayout != null)
                _cardLayout.gameObject.SetActive(false);
        }

        public override void OnInject()
        {
            base.OnInject();

            _cardLayout?.OnInject();
            _muliganWindow?.OnInject();

            // Resolve local player id for resource indicators
            int localPlayerId = -1;
            if (_battleContext != null && _battleContext.TryGetPlayer(out int playerEntity))
                localPlayerId = playerEntity;

            if (_resourceIndicators != null)
                foreach (var indicator in _resourceIndicators)
                    indicator?.OnInject(localPlayerId);

            _turnTimerView?.OnInject();
            _turnHintView?.OnInject();
            _endTurnButtonView?.OnInject();

            GameEventBus.Subscribe<OpponentCardPlayedUIEvent>(OnOpponentCardPlayed);
            GameEventBus.Subscribe<CardDetailUIEvent>(OnCardDetail);
            GameEventBus.Subscribe<PreStartPhaseBeginUIEvent>(OnPreStartPhaseBegin);
            GameEventBus.Subscribe<AuraStatusChangedUIEvent>(OnAuraStatusChanged);
        }

        public override void Unject()
        {
            GameEventBus.Unsubscribe<OpponentCardPlayedUIEvent>(OnOpponentCardPlayed);
            GameEventBus.Unsubscribe<CardDetailUIEvent>(OnCardDetail);
            GameEventBus.Unsubscribe<PreStartPhaseBeginUIEvent>(OnPreStartPhaseBegin);
            GameEventBus.Unsubscribe<AuraStatusChangedUIEvent>(OnAuraStatusChanged);

            _muliganWindow?.Unject();
            _cardLayout?.Unject();
            _cardLayout?.Dispose();

            if (_resourceIndicators != null)
                foreach (var indicator in _resourceIndicators)
                    indicator?.Unject();

            _turnTimerView?.Unject();
            _turnHintView?.Unject();
            _endTurnButtonView?.Unject();
        }

        public override void OnDipose()
        {
            Unject();
            base.OnDipose();
        }

        // ── Event handlers ────────────────────────────────────────────────────

        private void OnPreStartPhaseBegin(PreStartPhaseBeginUIEvent evt)
        {
            if (_cardLayout != null)
                _cardLayout.gameObject.SetActive(true);

            _cardLayout.OnPreStartPhaseBegin(evt);
        }

        private void OnOpponentCardPlayed(OpponentCardPlayedUIEvent evt)
        {
            _opponentCardPlayView?.Show(evt.Visual);
        }

        private void OnCardDetail(CardDetailUIEvent evt)
        {
            if (evt.Show) _cardDetailPopupView?.Show(evt.Visual);
            else          _cardDetailPopupView?.Hide();
        }

        private void OnAuraStatusChanged(AuraStatusChangedUIEvent evt)
            => _localAuraBarView?.SetAuras(evt.Visuals, evt.TurnsRemaining, evt.StackCounts);
    }
}
