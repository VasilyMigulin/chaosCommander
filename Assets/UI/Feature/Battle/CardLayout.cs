using AwesomeUI.Core.Layout;
using DG.Tweening;
using Game.Core.Events;
using Game.Core.Service;
using Game.Core.Shared;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AwesomeUI.Feature.Battle
{
    /// <summary>
    /// Layout руки игрока.
    /// — Первый слот всегда CommanderCardView (не уходит из руки).
    /// — Остальные SimpleCardView располагаются веером.
    /// — Click внутри Layout → expand (карты поднимаются и разворачиваются).
    /// — Click вне Layout → collapse (веер складывается, не мешает обзору).
    /// </summary>
    public class CardLayout : SourceLayout, IPointerClickHandler
    {
        [Header("Fan Settings")]
        [SerializeField] private float _fanAngleRange    = 30f;   // суммарный угол веера
        [SerializeField] private float _fanHeightCurve   = 40f;   // высота прогиба карт
        [SerializeField] private float _cardSpacing      = 90f;   // расстояние между картами
        [SerializeField] private float _animDuration     = 0.25f;

        [Header("Collapse")]
        [SerializeField] private float _collapsedCardY  = -80f;  // карты частично уходят вниз когда рука свёрнута
        [SerializeField] private float _expandedCardY   = 0f;   // базовый Y карт в развёрнутом состоянии

        [Header("Deal Animation")]
        [SerializeField] private float _dealInterval     = 0.18f; // задержка между картами
        [Tooltip("Локальный offset стартовой точки добора (откуда карта прилетает в руку). Обычно — направление колоды.")]
        [SerializeField] private Vector2 _dealInOffset   = new Vector2(500f, -300f);

        [Header("Discard Animation")]
        [SerializeField] private float _discardDuration  = 0.45f;

        [Header("Card Slots")]
        [SerializeField] private CommanderCardView _commanderSlot;
        [SerializeField] private List<SimpleCardView> _handSlots;

        private bool _isExpanded = false;
        private PlayCardView _selectedCard;
        private RectTransform _rectTransform;
        private Camera _uiCamera;

        // Порядок карт в руке слева-направо (новые добавляются в конец = к краю).
        // Веер строится по этому порядку, а не по индексу слота, поэтому при розыгрыше
        // карты из середины оставшиеся «сдвигаются», а добор всегда ложится с краю.
        private readonly List<SimpleCardView> _handOrder = new List<SimpleCardView>();

        // ── Deal Queue ────────────────────────────────────────────────────────
        private readonly Queue<CardAddedToHandUIEvent> _dealQueue = new Queue<CardAddedToHandUIEvent>();
        private bool _isDealCoroutineRunning = false;

        // ── Init ─────────────────────────────────────────────────────────────

        public override SourceLayout Init()
        {
            // не вызываем base.Init() — у нас своя структура слотов
            _rectTransform = GetComponent<RectTransform>();
            var canvas = GetComponentInParent<Canvas>();
            _uiCamera = canvas != null ? canvas.worldCamera : null;
            _slots = BuildSlotArray();

            foreach (var slot in _slots)
                slot.Init();

            gameObject.SetActive(true);
            return this;
        }

        private Core.Slot.SourceSlot[] BuildSlotArray()
        {
            var list = new List<Core.Slot.SourceSlot>();
            if (_commanderSlot != null) list.Add(_commanderSlot);
            foreach (var s in _handSlots)
                if (s != null) list.Add(s);
            return list.ToArray();
        }

        public override void OnInject()
        {
            GameEventBus.Unsubscribe<CardAddedToHandUIEvent>(OnCardAddedToHand);
            GameEventBus.Unsubscribe<CardRemovedFromHandUIEvent>(OnCardRemovedFromHand);
            GameEventBus.Unsubscribe<CardDiscardFromHandUIEvent>(OnCardDiscard);
            GameEventBus.Subscribe<CardAddedToHandUIEvent>(OnCardAddedToHand);
            GameEventBus.Subscribe<CardRemovedFromHandUIEvent>(OnCardRemovedFromHand);
            GameEventBus.Subscribe<CardDiscardFromHandUIEvent>(OnCardDiscard);

            if (_commanderSlot != null)
                _commanderSlot.OnInject();

            foreach (var slot in _handSlots)
                slot?.OnInject();
        }

        public void Unject()
        {
            GameEventBus.Unsubscribe<CardAddedToHandUIEvent>(OnCardAddedToHand);
            GameEventBus.Unsubscribe<CardRemovedFromHandUIEvent>(OnCardRemovedFromHand);
            GameEventBus.Unsubscribe<CardDiscardFromHandUIEvent>(OnCardDiscard);

            _commanderSlot?.Unject();
            foreach (var slot in _handSlots)
                slot?.Unject();
        }

        public override void Dispose()
        {
            Unject();
            base.Dispose();
        }

        // ── Event Handlers ───────────────────────────────────────────────────

        public void OnPreStartPhaseBegin(PreStartPhaseBeginUIEvent evt)
        {
            if (evt.HasCommander)
                EnqueueCard(evt.CommanderCard);

            if (evt.HandCards != null)
                foreach (var card in evt.HandCards)
                    EnqueueCard(card);
        }

        private void OnCardAddedToHand(CardAddedToHandUIEvent evt) => EnqueueCard(evt);

        // ── Deal Queue ────────────────────────────────────────────────────────

        private void EnqueueCard(CardAddedToHandUIEvent evt)
        {
            _dealQueue.Enqueue(evt);
            if (!_isDealCoroutineRunning)
                StartCoroutine(DealQueueCoroutine());
        }

        private IEnumerator DealQueueCoroutine()
        {
            _isDealCoroutineRunning = true;
            while (_dealQueue.Count > 0)
            {
                var evt = _dealQueue.Dequeue();
                PlaceCard(evt);
                yield return new WaitForSeconds(_dealInterval);
            }
            _isDealCoroutineRunning = false;
        }

        private void PlaceCard(CardAddedToHandUIEvent evt)
        {
            var data = new PlayCardData
            {
                CardEntity  = evt.CardEntity,
                NetworkKey  = evt.NetworkKey,
                CardName    = evt.CardName,
                Icon        = evt.Icon,
                CardType    = evt.CardType,
                Element     = evt.Element,
                Rarity      = evt.Rarity,
                IsCommander = evt.IsCommander,
                Visual      = evt.Visual,
            };

            Debug.Log($"[CardLayout] Place card entity {data.CardEntity}");

            if (evt.IsCommander)
            {
                _commanderSlot?.SetCard(data);
                return;
            }

            var freeSlot = GetFreeHandSlot();
            if (freeSlot == null)
            {
                Debug.LogWarning("[CardLayout] No free hand slots available");
                return;
            }

            // Pre-position: ставим слот в «точку добора» (откуда летит карта).
            var rt = freeSlot.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.DOKill();
                rt.localPosition = new Vector3(_dealInOffset.x, _dealInOffset.y, 0f);
                rt.localRotation = Quaternion.identity;
                rt.localScale    = Vector3.one;
            }

            freeSlot.SetCard(data);

            // ИНВАРИАНТ: новая карта = САМАЯ ПРАВАЯ. Остальные карты сдвигаются влево
            // (это делает RefreshFan через пересчёт позиций под новый count).
            // На всякий случай чистим возможный «хвост» от прошлой жизни этого слота.
            _handOrder.Remove(freeSlot);
            _handOrder.Add(freeSlot);

            RefreshFan();

            Debug.Log($"[CardLayout] Draw → slot '{freeSlot.name}' added to end. _handOrder ({_handOrder.Count}): {DescribeOrder()}");
        }

        private string DescribeOrder()
        {
            if (_handOrder.Count == 0) return "<empty>";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < _handOrder.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append($"[{i}]={_handOrder[i].name}({_handOrder[i].CardEntity})");
            }
            return sb.ToString();
        }

        // ── Discard ──────────────────────────────────────────────────────────

        private void OnCardDiscard(CardDiscardFromHandUIEvent evt)
        {
            foreach (var slot in _handSlots)
            {
                if (!slot.IsOccupied || slot.CardEntity != evt.CardEntity) continue;

                var capturedSlot = slot;
                capturedSlot.PlayDiscardAnimation(() =>
                {
                    capturedSlot.ClearCard();
                    _handOrder.Remove(capturedSlot);
                    RefreshFan();
                }, _discardDuration);
                return;
            }
        }

        private void OnCardRemovedFromHand(CardRemovedFromHandUIEvent evt)
        {
            foreach (var slot in _handSlots)
            {
                if (slot.IsOccupied && slot.CardEntity == evt.CardEntity)
                {
                    slot.ClearCard();
                    _handOrder.Remove(slot); // остальные сдвигаются при RefreshFan
                    RefreshFan();
                    return;
                }
            }
        }

        // ── Selection ────────────────────────────────────────────────────────

        /// <summary>Вызывается из PlayCardView.OnClick через CardLayout.</summary>
        public void SelectCard(PlayCardView card)
        {
            if (_selectedCard != null && _selectedCard != card)
                _selectedCard.Deselect();
            _selectedCard = card;
        }

        public void DeselectAll()
        {
            if (_selectedCard != null)
                _selectedCard.Deselect();
            _selectedCard = null;
        }

        // ── Expand / Collapse ────────────────────────────────────────────────

        public void OnPointerClick(PointerEventData eventData) => Toggle();

        private void Toggle()
        {
            if (_isExpanded) Collapse();
            else Expand();
        }

        private void Expand()
        {
            if (_isExpanded) return;
            _isExpanded = true;
            RefreshFan();
        }

        private void Collapse()
        {
            if (!_isExpanded) return;
            _isExpanded = false;
            DeselectAll();
            RefreshFan();
        }

        private void Update()
        {
            if (!_isExpanded) return;

            if (Input.GetMouseButtonDown(0) && !RectTransformUtility.RectangleContainsScreenPoint(_rectTransform, Input.mousePosition, _uiCamera))
            {
                Collapse();
            }
        }

        // ── Fan Layout ───────────────────────────────────────────────────────

        private void RefreshFan()
        {
            var activeSlots = GetAllActiveSlots();
            int count = activeSlots.Count;

            if (count == 0) return;

            float halfAngle = _fanAngleRange * 0.5f;
            float baseY     = _isExpanded ? _expandedCardY : _collapsedCardY;

            for (int i = 0; i < count; i++)
            {
                float t       = count > 1 ? (float)i / (count - 1) : 0.5f;
                float angle   = Mathf.Lerp(halfAngle, -halfAngle, t);
                float xOffset = Mathf.Lerp(-_cardSpacing * (count - 1) * 0.5f,
                                            _cardSpacing * (count - 1) * 0.5f, t);
                float yOffset = baseY + (_isExpanded
                    ? -_fanHeightCurve * Mathf.Abs(t - 0.5f) * 2f
                    : 0f);

                var rt = (activeSlots[i] as MonoBehaviour)?.GetComponent<RectTransform>();
                if (rt == null) continue;

                rt.DOLocalMove(new Vector3(xOffset, yOffset, 0f), _animDuration)
                  .SetEase(_isExpanded ? Ease.OutCubic : Ease.InCubic);
                rt.DOLocalRotate(new Vector3(0f, 0f, angle), _animDuration)
                  .SetEase(Ease.OutCubic);
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private SimpleCardView GetFreeHandSlot()
        {
            foreach (var slot in _handSlots)
                if (!slot.IsOccupied) return slot;
            return null;
        }

        private List<SimpleCardView> GetActiveHandSlots()
        {
            var result = new List<SimpleCardView>();
            foreach (var slot in _handSlots)
                if (slot.IsOccupied) result.Add(slot);
            return result;
        }

        private List<Component> GetAllActiveSlots()
        {
            var result = new List<Component>();
            if (_commanderSlot != null && _commanderSlot.IsOccupied)
                result.Add(_commanderSlot);
            // Порядок руки — по _handOrder (порядок добора), а не по индексу слота
            foreach (var slot in _handOrder)
                if (slot != null && slot.IsOccupied) result.Add(slot);
            return result;
        }
    }
}
