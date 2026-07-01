using DG.Tweening;
using Game.Core.Events;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Feature.Battle
{
    /// <summary>
    /// Таймер хода локального игрока.
    /// Показывается только во время LocalTurnStartedEvent.
    /// Считает обратный отсчёт, анимирует слайдер и меняет цвет при низком времени.
    /// </summary>
    public class TurnTimerView : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private TextMeshProUGUI _timerText;
        [SerializeField] private Slider          _progressSlider;
        [SerializeField] private Image           _sliderFill;
        [SerializeField] private CanvasGroup     _canvasGroup;

        [Header("Colors")]
        [SerializeField] private Color _normalColor  = Color.white;
        [SerializeField] private Color _warningColor = Color.yellow;
        [SerializeField] private Color _dangerColor  = Color.red;

        [Header("Thresholds")]
        [SerializeField] private float _warningThreshold = 0.4f;
        [SerializeField] private float _dangerThreshold  = 0.2f;

        private float _totalDuration;
        private float _remaining;
        private bool  _isRunning;
        private Tween _hideTween;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void Awake()
        {
            if (_canvasGroup == null) _canvasGroup = GetComponent<CanvasGroup>();
            gameObject.SetActive(false);
        }

        public void OnInject()
        {
            GameEventBus.Subscribe<LocalTurnStartedEvent>(OnLocalTurnStarted);
            GameEventBus.Subscribe<OpponentTurnEndedEvent>(OnOpponentTurn);
        }

        public void Unject()
        {
            GameEventBus.Unsubscribe<LocalTurnStartedEvent>(OnLocalTurnStarted);
            GameEventBus.Unsubscribe<OpponentTurnEndedEvent>(OnOpponentTurn);
        }

        // ── Unity update ──────────────────────────────────────────────────────

        private void Update()
        {
            if (!_isRunning) return;

            _remaining -= Time.deltaTime;
            if (_remaining < 0f) _remaining = 0f;

            UpdateDisplay();

            if (_remaining <= 0f)
                StopTimer();
        }

        // ── Event handlers ────────────────────────────────────────────────────

        private void OnLocalTurnStarted(LocalTurnStartedEvent evt)
        {
            _totalDuration = evt.TurnDurationSeconds;
            _remaining     = evt.TurnDurationSeconds;
            _isRunning     = true;

            _hideTween?.Kill();

            gameObject.SetActive(true);
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
                _canvasGroup.DOFade(1f, 0.3f);
            }

            UpdateDisplay();
        }

        // Локальный игрок передал ход (стал пассивным) → гасим таймер.
        private void OnOpponentTurn(OpponentTurnEndedEvent evt)
        {
            StopTimer();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void StopTimer()
        {
            _isRunning = false;

            if (_canvasGroup != null)
            {
                _hideTween = _canvasGroup.DOFade(0f, 0.3f)
                    .OnComplete(() => gameObject.SetActive(false));
            }
            else
            {
                gameObject.SetActive(false);
            }
        }

        private void UpdateDisplay()
        {
            int seconds = Mathf.CeilToInt(_remaining);

            if (_timerText != null)
                _timerText.text = seconds.ToString();

            if (_progressSlider != null && _totalDuration > 0f)
                _progressSlider.value = _remaining / _totalDuration;

            if (_sliderFill != null)
            {
                float ratio = _totalDuration > 0f ? _remaining / _totalDuration : 1f;

                if (ratio <= _dangerThreshold)
                    _sliderFill.color = _dangerColor;
                else if (ratio <= _warningThreshold)
                    _sliderFill.color = _warningColor;
                else
                    _sliderFill.color = _normalColor;
            }
        }

        private void OnDestroy()
        {
            _hideTween?.Kill();
        }
    }
}
