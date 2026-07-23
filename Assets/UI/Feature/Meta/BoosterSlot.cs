using System;
using System.Collections;
using AwesomeUI.Core.Slot;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// Слот бустера в инвентаре: иконка + название + количество + «Открыть».
    /// ТАП — открыть один. УДЕРЖАНИЕ (long-press) при наличии ≥2 бустеров — окно выбора «сколько открыть»
    /// (мульти-открытие до 5), панель ловит onHold и показывает попап.
    ///
    /// Префаб: Button на корне (IsButton=true, EnableIcon=false), _iconImage, _nameText, _countText ("x3").
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class BoosterSlot : SourceSlot, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [SerializeField] private Image _iconImage;
        [SerializeField] private TextMeshProUGUI _nameText;
        [SerializeField] private TextMeshProUGUI _countText;

        const float HoldThreshold = 1f;   // сек до срабатывания long-press (мульти-открытие)

        string _itemId;
        int _count;
        Action<string> _onOpen;
        Action<string, int> _onHold;   // (itemId, ownedCount) → окно мульти-открытия
        Coroutine _holdRoutine;
        bool _holdFired;

        public void SetData(string itemId, string displayName, int count, Sprite icon,
            Action<string> onOpen, Action<string, int> onHold = null)
        {
            _itemId = itemId;
            _count = count;
            _onOpen = onOpen;
            _onHold = onHold;

            if (_iconImage != null)
            {
                _iconImage.sprite = icon;
                _iconImage.enabled = icon != null;
            }
            if (_nameText != null) _nameText.text = displayName;
            UpdateView();
        }

        public override void UpdateView()
        {
            if (_countText != null) _countText.text = $"x{_count}";
            if (_btnClick != null) _btnClick.interactable = _count > 0;
        }

        // ── Long-press ───────────────────────────────────────────────────────────

        public void OnPointerDown(PointerEventData eventData)
        {
            _holdFired = false;
            // Мульти-открытие имеет смысл только при ≥2 бустерах.
            if (_count > 1 && _onHold != null) _holdRoutine = StartCoroutine(HoldTimer());
        }

        public void OnPointerUp(PointerEventData eventData) => CancelHold();
        public void OnPointerExit(PointerEventData eventData) => CancelHold();

        IEnumerator HoldTimer()
        {
            yield return new WaitForSeconds(HoldThreshold);
            _holdRoutine = null;
            _holdFired = true;
            _onHold?.Invoke(_itemId, _count);   // открыть окно выбора количества
        }

        void CancelHold()
        {
            if (_holdRoutine != null) { StopCoroutine(_holdRoutine); _holdRoutine = null; }
        }

        // ── Клик ───────────────────────────────────────────────────────────────

        public override void OnClick()
        {
            if (_holdFired) { _holdFired = false; return; }   // это было удержание — одиночное открытие не запускаем
            if (_count <= 0) return;
            _onOpen?.Invoke(_itemId);
        }

        public override void OnUse() { }

        public override void Unject()
        {
            CancelHold();
            _onOpen = null;
            _onHold = null;
        }

        public override void Dispose()
        {
            Unject();
            base.Dispose();
        }
    }
}
