using System;
using System.Collections.Generic;
using Game.Core.Shared;
using UnityEngine;

namespace AwesomeUI.Feature.Battle
{
    /// <summary>
    /// Статус-бар активных аур (чар на поле) одного игрока — ряд миниатюр (AuraStatusSlotView) + счётчик
    /// ходов, удержание открывает CardDetailPopupView через CardDetailUIEvent (слот публикует событие, не
    /// держит прямую ссылку — см. AuraStatusSlotView, попап живёт в другом префабе).
    ///
    /// НЕ подписывается на GameEventBus сам — ТОЛЬКО прямой SetAuras(...) (IAuraStatusReceiver). Причина:
    /// это ОДИН класс на ДВА одновременно живых инстанса (локальный бар в BattlePanel + бар на аватаре
    /// оппонента) — GameEventBus глобальный, событие AuraStatusChangedUIEvent летит ВСЕМ подписчикам сразу,
    /// поэтому если бы каждый инстанс сам подписывался, локальные данные (единственные, что летят по шине)
    /// прилетали бы ОБОИМ барам и перетирали верные данные оппонента (гонка с прямым SetAuras от
    /// PlayerStatsViewSystem — баг, воспроизведённый на практике). Подписку и форвардинг локального события
    /// держит BattlePanel (тот же приём, что и у CardDetailUIEvent → _cardDetailPopupView).
    /// </summary>
    public class AuraStatusBarView : MonoBehaviour, IAuraStatusReceiver
    {
        [Tooltip("Слоты миниатюр — капом число активных аур, которые можно показать одновременно.")]
        [SerializeField] private List<AuraStatusSlotView> _slots;

        // null (не Array.Empty) — «ещё ни разу не рисовали»: первый SetAuras(...) должен отрисоваться, ДАЖЕ
        // если аур сейчас 0 (иначе пустой входящий массив совпадёт с дефолтным пустым и Redraw/Clear слотов
        // ни разу не вызовется — слоты останутся в состоянии, в котором их оставил редактор сцены).
        private CardVisualData[] _lastVisuals;
        private int[] _lastTurns;
        private int[] _lastCounts;

        /// <summary>Вызывается напрямую: локальный — из BattlePanel (форвард AuraStatusChangedUIEvent),
        /// оппонент — из AvatarPlayerView.SetAuras (PlayerStatsViewSystem пушит каждый кадр).
        /// turns[i] соответствует visuals[i]; &lt;0 — постоянная чара. counts[i] — стак одноимённых (×N).
        /// Данные приходят уже сгруппированными и отсортированными «скоро истекут — первыми»
        /// (см. PlayerStatsViewSystem.GatherAuras) — при нехватке слотов лишние просто не показываются.</summary>
        public void SetAuras(CardVisualData[] visuals, int[] turns, int[] counts)
        {
            visuals ??= Array.Empty<CardVisualData>();
            turns   ??= Array.Empty<int>();
            counts  ??= Array.Empty<int>();
            if (SameAs(visuals, turns, counts)) return;

            _lastVisuals = visuals;
            _lastTurns = turns;
            _lastCounts = counts;
            Redraw(visuals, turns, counts);
        }

        private void Redraw(CardVisualData[] visuals, int[] turns, int[] counts)
        {
            if (_slots == null) return;

            for (int i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                if (slot == null) continue;

                if (i < visuals.Length) slot.Setup(visuals[i], turns[i], i < counts.Length ? counts[i] : 1);
                else slot.Clear();
            }
        }

        private bool SameAs(CardVisualData[] visuals, int[] turns, int[] counts)
        {
            if (_lastVisuals == null) return false;   // ещё не рисовали — форсим первый Redraw
            if (visuals.Length != _lastVisuals.Length) return false;
            for (int i = 0; i < visuals.Length; i++)
                if (turns[i] != _lastTurns[i]
                    || (i < counts.Length && i < _lastCounts.Length && counts[i] != _lastCounts[i])
                    || visuals[i].CardName != _lastVisuals[i].CardName) return false;
            return true;
        }
    }
}
