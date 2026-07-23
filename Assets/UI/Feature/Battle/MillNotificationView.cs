using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using Game.Core.Events;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Feature.Battle
{
    /// <summary>
    /// Уведомление о сжигании карты из колоды (Mill).
    /// Один MonoBehaviour, висящий на оверлей-канвасе. Слушает CardMillFromDeckUIEvent
    /// и поочерёдно показывает каждую сожжённую карту: появляется, поднимается, гаснет.
    ///
    /// Поля заполняются в инспекторе:
    ///   _root        — основной CanvasGroup (для fade).
    ///   _icon        — Image, куда подставляется арт карты.
    ///   _nameText    — TMP_Text с названием.
    ///   _captionText — опциональный TMP_Text «Сожжено» (статичный).
    ///
    /// События обрабатываются по очереди — если в один кадр пришли несколько,
    /// они проиграются последовательно (одно за другим).
    /// </summary>
    public class MillNotificationView : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CanvasGroup _root;
        [SerializeField] private Image       _icon;
        [SerializeField] private TMP_Text    _nameText;
        [SerializeField] private TMP_Text    _captionText;

        [Header("Animation")]
        [SerializeField] private float   _fadeInDuration  = 0.25f;
        [SerializeField] private float   _holdDuration    = 1.10f;
        [SerializeField] private float   _fadeOutDuration = 0.35f;
        [SerializeField] private float   _floatUpDistance = 60f;
        [SerializeField] private string  _captionDefault  = "Сожжено";

        private RectTransform _rect;
        private Vector3       _basePosition;
        private bool          _subscribed;

        private readonly Queue<CardMillFromDeckUIEvent> _queue = new Queue<CardMillFromDeckUIEvent>();
        private bool _playing;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void Awake()
        {
            _rect = (RectTransform)transform;
            _basePosition = _rect.localPosition;
            if (_root != null) _root.alpha = 0f;
            gameObject.SetActive(false);

            if (_captionText != null && string.IsNullOrEmpty(_captionText.text))
                _captionText.text = _captionDefault;
        }

        private void OnEnable()  => Subscribe();
        private void OnDisable() => Unsubscribe();
        private void OnDestroy() => Unsubscribe();

        // SubscribePersistent, не Subscribe: объект persistent (DontDestroyOnLoad), OnEnable отрабатывает
        // ОДИН раз за сессию — обычная подписка терялась бы навсегда после первого GameEventBus.Clear()
        // (см. EcsRunHandler.Dispose при выходе из боя).
        private void Subscribe()
        {
            if (_subscribed) return;
            _subscribed = true;
            GameEventBus.SubscribePersistent<CardMillFromDeckUIEvent>(OnMill);
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            _subscribed = false;
            GameEventBus.UnsubscribePersistent<CardMillFromDeckUIEvent>(OnMill);
        }

        private void OnMill(CardMillFromDeckUIEvent evt)
        {
            _queue.Enqueue(evt);
            if (!_playing) StartCoroutine(PlayQueue());
        }

        // ── Render ────────────────────────────────────────────────────────────

        private IEnumerator PlayQueue()
        {
            _playing = true;
            while (_queue.Count > 0)
            {
                var evt = _queue.Dequeue();
                yield return PlayOne(evt);
            }
            _playing = false;
        }

        private IEnumerator PlayOne(CardMillFromDeckUIEvent evt)
        {
            // Заполняем визуал
            if (_icon != null)
            {
                _icon.sprite  = evt.Icon;
                _icon.enabled = evt.Icon != null;
            }
            if (_nameText != null)
                _nameText.text = string.IsNullOrEmpty(evt.CardName) ? "—" : evt.CardName;

            // Подпись «Сожжено» — локализуем при каждом показе (берёт текущий язык).
            if (_captionText != null)
                _captionText.text = Game.Core.Shared.CardTextLocalization.GetText("ui.battle.milled", _captionDefault);

            gameObject.SetActive(true);
            _rect.DOKill();
            if (_root != null) _root.alpha = 0f;
            _rect.localPosition = _basePosition;

            // Fade in + рост
            if (_root != null) _root.DOFade(1f, _fadeInDuration).SetEase(Ease.OutCubic);
            _rect.localScale = Vector3.one * 0.85f;
            _rect.DOScale(1f, _fadeInDuration).SetEase(Ease.OutCubic);

            yield return new WaitForSeconds(_fadeInDuration + _holdDuration);

            // Float up + fade out
            _rect.DOLocalMoveY(_basePosition.y + _floatUpDistance, _fadeOutDuration).SetEase(Ease.InCubic);
            if (_root != null) _root.DOFade(0f, _fadeOutDuration).SetEase(Ease.InCubic);

            yield return new WaitForSeconds(_fadeOutDuration);

            _rect.localPosition = _basePosition;
            gameObject.SetActive(false);
        }
    }
}
