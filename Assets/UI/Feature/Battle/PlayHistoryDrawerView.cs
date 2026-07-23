using System.Collections.Generic;
using DG.Tweening;
using Game.Core.Events;
using Game.Core.Shared;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AwesomeUI.Feature.Battle
{
    /// <summary>
    /// Свайп-история последних розыгрышей. Панель (_panel) сдвигается по X между _closedX (спрятана,
    /// виден только «язычок»-хендл) и _openX (полностью видна) — драг двигает панель за пальцем,
    /// отпускание снэпит в ближайшее состояние (доля пройденного пути против _snapThreshold).
    ///
    /// Источник данных — ТОТ ЖЕ OpponentCardPlayedUIEvent, что и всплывающий выкат розыгрыша (см. память
    /// project_build_debug_map.md, «Единый фид розыгрышей») — событие публикуется на ЛЮБОЙ каст (свой/
    /// чужой/авто), поэтому история заполняется без отдельного ECS-источника. Капом _thumbSlots.Count,
    /// FIFO (старые вытесняются). Удержание на миниатюре — общий CardDetailPopupView (см.
    /// PlayHistoryThumbView) — тот же попап, что и у существа на столе; тип карты (существо/спелл/чара)
    /// ему не важен, он просто рендерит CardVisualData.
    /// </summary>
    public class PlayHistoryDrawerView : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [Header("Panel drag")]
        [SerializeField] private RectTransform _panel;
        [SerializeField] private float _closedX = -360f;   // спрятана (виден только хендл)
        [SerializeField] private float _openX   = 0f;       // полностью видна
        [Range(0.05f, 0.95f)]
        [SerializeField] private float _snapThreshold = 0.5f;
        [SerializeField] private float _snapDuration = 0.25f;

        [Header("Data")]
        [SerializeField] private CardDetailPopupView _detailPopup;
        [Tooltip("Слоты миниатюр (порядок — старые→новые слева направо). Капом истории = число слотов.")]
        [SerializeField] private List<PlayHistoryThumbView> _thumbSlots;

        private readonly Queue<CardVisualData> _history = new Queue<CardVisualData>();
        private Tween _snapTween;

        private int Capacity => _thumbSlots != null ? _thumbSlots.Count : 0;

        private void OnEnable()
        {
            // SubscribePersistent, не Subscribe: объект persistent (DontDestroyOnLoad), OnEnable отрабатывает
            // ОДИН раз за сессию — обычная подписка терялась бы навсегда после первого GameEventBus.Clear()
            // (см. EcsRunHandler.Dispose при выходе из боя), и история розыгрышей переставала бы наполняться
            // со 2-го матча.
            GameEventBus.SubscribePersistent<OpponentCardPlayedUIEvent>(OnCardPlayed);
            // Новый матч — старая история розыгрышей больше не актуальна (иначе карты ПРЕДЫДУЩЕГО боя
            // висели бы в ленте следующего). MulliganPhaseBeginUIEvent — самое раннее событие, стреляющее
            // на КАЖДЫЙ матч (в отличие от OnEnable, который отработает только один раз за сессию).
            GameEventBus.SubscribePersistent<MulliganPhaseBeginUIEvent>(OnNewMatchStarting);
            if (_panel != null)
            {
                var pos = _panel.anchoredPosition;
                pos.x = _closedX;
                _panel.anchoredPosition = pos;
            }
        }

        private void OnDisable()
        {
            GameEventBus.UnsubscribePersistent<OpponentCardPlayedUIEvent>(OnCardPlayed);
            GameEventBus.UnsubscribePersistent<MulliganPhaseBeginUIEvent>(OnNewMatchStarting);
            _snapTween?.Kill();
        }

        private void OnNewMatchStarting(MulliganPhaseBeginUIEvent _)
        {
            _history.Clear();
            Redraw();
        }

        private void OnCardPlayed(OpponentCardPlayedUIEvent evt)
        {
            if (Capacity <= 0) return;

            _history.Enqueue(evt.Visual);
            while (_history.Count > Capacity) _history.Dequeue();

            Redraw();
        }

        private void Redraw()
        {
            var arr = _history.ToArray();   // старые→новые, длина <= Capacity
            int offset = _thumbSlots.Count - arr.Length;   // правим слоты справа налево, пустые слева

            for (int i = 0; i < _thumbSlots.Count; i++)
            {
                var slot = _thumbSlots[i];
                if (slot == null) continue;

                int dataIndex = i - offset;
                if (dataIndex >= 0 && dataIndex < arr.Length) slot.Setup(_detailPopup, arr[dataIndex]);
                else slot.Clear();
            }
        }

        // ── Drag (свайп) ─────────────────────────────────────────────────────────
        public void OnBeginDrag(PointerEventData eventData) => _snapTween?.Kill();

        public void OnDrag(PointerEventData eventData)
        {
            if (_panel == null) return;

            var pos = _panel.anchoredPosition;
            float min = Mathf.Min(_closedX, _openX);
            float max = Mathf.Max(_closedX, _openX);
            pos.x = Mathf.Clamp(pos.x + eventData.delta.x, min, max);
            _panel.anchoredPosition = pos;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (_panel == null) return;

            float t = Mathf.InverseLerp(_closedX, _openX, _panel.anchoredPosition.x);
            SnapTo(t >= _snapThreshold);
        }

        private void SnapTo(bool open)
        {
            _snapTween?.Kill();
            _snapTween = _panel.DOAnchorPosX(open ? _openX : _closedX, _snapDuration).SetEase(Ease.OutCubic);
        }

        private void OnDestroy() => _snapTween?.Kill();
    }
}
