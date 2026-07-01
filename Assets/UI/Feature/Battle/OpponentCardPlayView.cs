using System.Collections.Generic;
using AwesomeUI.Core.Card;
using DG.Tweening;
using Game.Core.Shared;
using UnityEngine;

namespace AwesomeUI.Feature.Battle
{
    /// <summary>
    /// Карта, которую разыграл оппонент — ПОЛНОЦЕННАЯ карта (наследник CardBaseView со всеми полями:
    /// банер/рарность-фон/арт/имя/тип/стоимость/статы/описание) + анимация выката слева → зависание → уход.
    /// Префаб строится как обычная карта (как CardPickupView), только с этим компонентом + CanvasGroup.
    /// ОЧЕРЕДЬ: оппонент может разыгрывать быстро — карты копятся и показываются по одной; при наполненной
    /// очереди зависание укорачивается. Вызывается через Show(visual) из BattlePanel по событию.
    /// </summary>
    public class OpponentCardPlayView : CardBaseView
    {
        [Header("Opponent Play — Animation")]
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private float _fadeInDuration  = 0.3f;
        [SerializeField] private float _holdDuration    = 1.4f;
        [SerializeField] private float _fadeOutDuration = 0.3f;
        [SerializeField] private float _slideDistance   = 220f;   // выкат слева (по X, в anchored-единицах)

        private RectTransform _rt;
        private Vector2 _restPos;
        private bool _restCaptured;
        private Sequence _sequence;

        private readonly Queue<CardVisualData> _queue = new Queue<CardVisualData>();
        private bool _playing;

        // ── SourceSlot: обязательные абстрактные методы (карта дисплейная, без взаимодействия) ──
        public override void Unject()     { }
        public override void OnUse()      { }
        public override void OnClick()    { }
        public override void UpdateView() { }

        private void Awake()
        {
            _rt = (RectTransform)transform;
            CaptureRest();
            if (_canvasGroup == null) _canvasGroup = GetComponent<CanvasGroup>();
            gameObject.SetActive(false);
        }

        // Запоминаем «домашнюю» позицию ОДИН раз (до первого сдвига), чтобы повторные показы не дрейфовали.
        private void CaptureRest()
        {
            if (_restCaptured) return;
            if (_rt == null) _rt = (RectTransform)transform;
            _restPos = _rt.anchoredPosition;
            _restCaptured = true;
        }

        /// <summary>Поставить карту оппонента в очередь показа.</summary>
        public void Show(in CardVisualData visual)
        {
            _queue.Enqueue(visual);
            if (!_playing) PlayNext();
        }

        private void PlayNext()
        {
            if (_queue.Count == 0)
            {
                _playing = false;
                gameObject.SetActive(false);
                return;
            }

            _playing = true;
            CaptureRest();
            var visual = _queue.Dequeue();

            ApplyVisualData(visual);   // полный рендер карты (поля CardBaseView)

            gameObject.SetActive(true);
            if (_canvasGroup != null) _canvasGroup.alpha = 0f;
            Vector2 offLeft = _restPos - new Vector2(_slideDistance, 0f);
            _rt.anchoredPosition = offLeft;

            // Очередь ждёт → держим карту меньше, чтобы хвост быстрее разгрёбся.
            float hold = _queue.Count > 0 ? _holdDuration * 0.4f : _holdDuration;

            _sequence?.Kill();
            _sequence = DOTween.Sequence();
            // выкат слева + появление
            _sequence.Append(_rt.DOAnchorPos(_restPos, _fadeInDuration).SetEase(Ease.OutCubic));
            if (_canvasGroup != null) _sequence.Join(_canvasGroup.DOFade(1f, _fadeInDuration).SetEase(Ease.OutQuad));
            // зависание
            _sequence.AppendInterval(hold);
            // уход обратно влево + исчезание
            _sequence.Append(_rt.DOAnchorPos(offLeft, _fadeOutDuration).SetEase(Ease.InCubic));
            if (_canvasGroup != null) _sequence.Join(_canvasGroup.DOFade(0f, _fadeOutDuration).SetEase(Ease.InQuad));
            // следующая карта из очереди
            _sequence.OnComplete(PlayNext);
        }

        private void OnDestroy() => _sequence?.Kill();
    }
}
