using DG.Tweening;
using Game.Core.Events;
using TMPro;
using UnityEngine;

namespace AwesomeUI.Feature.Battle
{
    /// <summary>
    /// Хинт смены хода.
    /// «Ваш ход» — при LocalTurnStartedEvent.
    /// «Ход оппонента» — при OpponentTurnEndedEvent (= наш ход закончился → начался ход оппонента).
    /// Каждый хинт появляется, зависает и пропадает.
    /// </summary>
    public class TurnHintView : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CanvasGroup     _canvasGroup;
        [SerializeField] private TextMeshProUGUI _hintText;

        [Header("Texts")]
        [SerializeField] private string _yourTurnText     = "Ваш ход!";
        [SerializeField] private string _opponentTurnText = "Ход оппонента";

        [Header("Animation")]
        [SerializeField] private float _fadeIn    = 0.3f;
        [SerializeField] private float _hold      = 1.5f;
        [SerializeField] private float _fadeOut   = 0.3f;
        [SerializeField] private float _scaleFrom = 0.7f;

        private Sequence _sequence;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void Awake()
        {
            if (_canvasGroup == null) _canvasGroup = GetComponent<CanvasGroup>();
            gameObject.SetActive(false);
        }

        public void OnInject()
        {
            GameEventBus.Subscribe<LocalTurnStartedEvent>(OnLocalTurnStarted);
            GameEventBus.Subscribe<OpponentTurnEndedEvent>(OnOpponentTurnEnded);
        }

        public void Unject()
        {
            GameEventBus.Unsubscribe<LocalTurnStartedEvent>(OnLocalTurnStarted);
            GameEventBus.Unsubscribe<OpponentTurnEndedEvent>(OnOpponentTurnEnded);
        }

        // ── Event handlers ────────────────────────────────────────────────────

        private void OnLocalTurnStarted(LocalTurnStartedEvent evt)
        {
            ShowHint(_yourTurnText);
        }

        private void OnOpponentTurnEnded(OpponentTurnEndedEvent evt)
        {
            ShowHint(_opponentTurnText);
        }

        // ── Animation ─────────────────────────────────────────────────────────

        private void ShowHint(string text)
        {
            _sequence?.Kill();

            if (_hintText != null) _hintText.text = text;

            gameObject.SetActive(true);
            transform.localScale = Vector3.one * _scaleFrom;
            _canvasGroup.alpha   = 0f;

            _sequence = DOTween.Sequence();
            _sequence.Append(_canvasGroup.DOFade(1f, _fadeIn).SetEase(Ease.OutQuad));
            _sequence.Join(transform.DOScale(1f, _fadeIn).SetEase(Ease.OutBack));
            _sequence.AppendInterval(_hold);
            _sequence.Append(_canvasGroup.DOFade(0f, _fadeOut).SetEase(Ease.InQuad));
            _sequence.OnComplete(() => gameObject.SetActive(false));
        }

        private void OnDestroy()
        {
            _sequence?.Kill();
        }
    }
}
