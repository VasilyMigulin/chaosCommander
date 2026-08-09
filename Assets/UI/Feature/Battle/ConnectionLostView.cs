using DG.Tweening;
using Game.Core.Events;
using TMPro;
using UnityEngine;

namespace AwesomeUI.Feature.Battle
{
    /// <summary>
    /// Баннер проблем соединения в MP-бою (NetConnectionWatchSystem):
    ///   «Соперник не отвечает» / «Соперник отключился» / «Соединение потеряно» + обратный отсчёт до
    /// технического завершения. ConnectionIssueUIEvent приходит раз в секунду, пока проблема активна;
    /// ConnectionRestoredUIEvent — сигналы вернулись, баннер прячется; MatchEndedEvent — матч закрыт
    /// (в т.ч. техрезультатом) — баннер мгновенно уступает место поп-апу результата.
    /// Висит ПОКА проблема активна (в отличие от TurnHintView не авто-скрывается).
    /// Подключение как у остальных вью боя: BattlePanel держит ссылку и зовёт OnInject/Unject.
    /// </summary>
    public class ConnectionLostView : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CanvasGroup     _canvasGroup;
        [SerializeField] private TextMeshProUGUI _titleText;
        [SerializeField] private TextMeshProUGUI _countdownText;

        [Header("Texts (фоллбэки; локализация ui.battle.conn.*)")]
        [SerializeField] private string _opponentSilentText = "Соперник не отвечает…";
        [SerializeField] private string _opponentLeftText   = "Соперник отключился";
        [SerializeField] private string _connectionLostText = "Соединение потеряно";
        [SerializeField] private string _countdownFormat    = "Матч завершится через {0} с";

        [Header("Animation")]
        [SerializeField] private float _fadeIn  = 0.25f;
        [SerializeField] private float _fadeOut = 0.25f;

        private Tween _fade;
        private bool  _shown;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void Awake()
        {
            if (_canvasGroup == null) _canvasGroup = GetComponent<CanvasGroup>();
            gameObject.SetActive(false);
        }

        public void OnInject()
        {
            GameEventBus.Subscribe<ConnectionIssueUIEvent>(OnIssue);
            GameEventBus.Subscribe<ConnectionRestoredUIEvent>(OnRestored);
            GameEventBus.Subscribe<MatchEndedEvent>(OnMatchEnded);
        }

        public void Unject()
        {
            GameEventBus.Unsubscribe<ConnectionIssueUIEvent>(OnIssue);
            GameEventBus.Unsubscribe<ConnectionRestoredUIEvent>(OnRestored);
            GameEventBus.Unsubscribe<MatchEndedEvent>(OnMatchEnded);
        }

        // ── Event handlers ────────────────────────────────────────────────────

        private void OnIssue(ConnectionIssueUIEvent evt)
        {
            if (_titleText != null) _titleText.text = TitleFor(evt.Kind);
            if (_countdownText != null)
                _countdownText.text = string.Format(
                    Game.Core.Shared.CardTextLocalization.GetText("ui.battle.conn.countdown", _countdownFormat),
                    evt.SecondsLeft);

            if (!_shown) Show();
        }

        private void OnRestored(ConnectionRestoredUIEvent evt) => Hide(instant: false);

        // Матч закрыт (штатно или техрезультатом) — мгновенно уходим, сцену занимает поп-ап результата.
        private void OnMatchEnded(MatchEndedEvent evt) => Hide(instant: true);

        private string TitleFor(ConnectionIssueKind kind) => kind switch
        {
            ConnectionIssueKind.OpponentLeft   => Game.Core.Shared.CardTextLocalization.GetText("ui.battle.conn.left",   _opponentLeftText),
            ConnectionIssueKind.ConnectionLost => Game.Core.Shared.CardTextLocalization.GetText("ui.battle.conn.lost",   _connectionLostText),
            _                                  => Game.Core.Shared.CardTextLocalization.GetText("ui.battle.conn.silent", _opponentSilentText),
        };

        // ── Animation ─────────────────────────────────────────────────────────

        private void Show()
        {
            _shown = true;
            _fade?.Kill();
            gameObject.SetActive(true);
            _canvasGroup.alpha = 0f;
            _fade = _canvasGroup.DOFade(1f, _fadeIn).SetEase(Ease.OutQuad);
        }

        private void Hide(bool instant)
        {
            if (!_shown) return;
            _shown = false;
            _fade?.Kill();

            if (instant || !gameObject.activeInHierarchy)
            {
                _canvasGroup.alpha = 0f;
                gameObject.SetActive(false);
                return;
            }

            _fade = _canvasGroup.DOFade(0f, _fadeOut).SetEase(Ease.InQuad)
                                .OnComplete(() => gameObject.SetActive(false));
        }

        private void OnDestroy() => _fade?.Kill();
    }
}
