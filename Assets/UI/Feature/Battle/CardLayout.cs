using AwesomeUI.Core.Layout;
using DG.Tweening;
using Game.Core.Events;
using Game.Core.Service;
using Game.Core.Shared;
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
    public class CardLayout : SourceLayout, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        [Header("Fan Settings")]
        [SerializeField] private float _fanAngleRange    = 30f;   // суммарный угол веера
        [SerializeField] private float _fanHeightCurve   = 40f;   // высота прогиба карт
        [SerializeField] private float _cardSpacing      = 90f;   // расстояние между картами
        [SerializeField] private float _animDuration     = 0.25f;

        [Header("Collapse")]
        [SerializeField] private float _collapsedY       = -120f; // смещение вниз при свёрнутом состоянии
        [SerializeField] private float _expandedY        = 0f;

        [Header("Card Slots")]
        [SerializeField] private CommanderCardView _commanderSlot;
        [SerializeField] private List<SimpleCardView> _handSlots;

        private bool _isExpanded = false;
        private PlayCardView _selectedCard;

        // ── Init ─────────────────────────────────────────────────────────────

        public override SourceLayout Init()
        {
            // не вызываем base.Init() — у нас своя структура слотов
            _slots = BuildSlotArray();

            foreach (var slot in _slots)
                slot.Init();

            gameObject.SetActive(true);
            return this;
        }

        private AwesomeUI.Core.Slot.SourceSlot[] BuildSlotArray()
        {
            var list = new List<AwesomeUI.Core.Slot.SourceSlot>();
            if (_commanderSlot != null) list.Add(_commanderSlot);
            foreach (var s in _handSlots)
                if (s != null) list.Add(s);
            return list.ToArray();
        }

        public override void OnInject()
        {
            GameEventBus.Subscribe<CardAddedToHandUIEvent>(OnCardAddedToHand);
            GameEventBus.Subscribe<CardRemovedFromHandUIEvent>(OnCardRemovedFromHand);

            if (_commanderSlot != null)
                _commanderSlot.OnInject();
        }

        public override void Dispose()
        {
            GameEventBus.Unsubscribe<CardAddedToHandUIEvent>(OnCardAddedToHand);
            GameEventBus.Unsubscribe<CardRemovedFromHandUIEvent>(OnCardRemovedFromHand);

            base.Dispose();
        }

        // ── Event Handlers ───────────────────────────────────────────────────

        private void OnCardAddedToHand(CardAddedToHandUIEvent evt)
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
            };

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

            freeSlot.SetCard(data);
            RefreshFan();
        }

        private void OnCardRemovedFromHand(CardRemovedFromHandUIEvent evt)
        {
            foreach (var slot in _handSlots)
            {
                if (slot.IsOccupied && slot.CardEntity == evt.CardEntity)
                {
                    slot.ClearCard();
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

        public void OnPointerEnter(PointerEventData eventData) => Expand();
        public void OnPointerExit(PointerEventData eventData)  => Collapse();
        public void OnPointerClick(PointerEventData eventData) => Expand();

        private void Expand()
        {
            if (_isExpanded) return;
            _isExpanded = true;
            transform.DOLocalMoveY(_expandedY, _animDuration).SetEase(Ease.OutBack);
            RefreshFan();
        }

        private void Collapse()
        {
            if (!_isExpanded) return;
            _isExpanded = false;
            DeselectAll();
            transform.DOLocalMoveY(_collapsedY, _animDuration).SetEase(Ease.InBack);
            RefreshFan();
        }

        // ── Fan Layout ───────────────────────────────────────────────────────

        private void RefreshFan()
        {
            var activeSlots = GetActiveHandSlots();
            int count = activeSlots.Count;

            if (count == 0) return;

            float halfAngle = _fanAngleRange * 0.5f;

            for (int i = 0; i < count; i++)
            {
                float t        = count > 1 ? (float)i / (count - 1) : 0.5f;
                float angle    = Mathf.Lerp(halfAngle, -halfAngle, t);
                float xOffset  = Mathf.Lerp(-_cardSpacing * (count - 1) * 0.5f,
                                             _cardSpacing * (count - 1) * 0.5f, t);
                float yOffset  = _isExpanded
                    ? -_fanHeightCurve * Mathf.Abs(t - 0.5f) * 2f
                    : 0f;

                var rt = activeSlots[i].GetComponent<RectTransform>();
                if (rt == null) continue;

                rt.DOLocalMove(new Vector3(xOffset, yOffset, 0f), _animDuration)
                  .SetEase(Ease.OutCubic);
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
    }
}
