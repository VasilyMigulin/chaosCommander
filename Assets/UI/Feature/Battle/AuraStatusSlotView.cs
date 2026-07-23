using AwesomeUI.Core.Card;
using Game.Core.Events;
using Game.Core.Shared;
using TMPro;
using UnityEngine;

namespace AwesomeUI.Feature.Battle
{
    /// <summary>
    /// Один слот статус-бара активных аур (чары на поле) — миниатюра карты + счётчик оставшихся ходов.
    /// Обычная CardBaseView-карта, переиспользует уже готовый hold-детект базового класса: удержание
    /// показывает ТОТ ЖЕ попап (CardDetailPopupView), что и удержание на существе на столе.
    ///
    /// В отличие от PlayHistoryThumbView (прямая ссылка на CardDetailPopupView — оба живут в ОДНОМ префабе
    /// BattlePanel), этот слот дублируется в ДВУХ разных префабах (BattlePanel — локальный, AvatarPrefabView —
    /// оппонент), а попап живёт только внутри BattlePanel — прямую ссылку туда из другого префаба перетащить
    /// нельзя. Поэтому — bus-событие CardDetailUIEvent, как у CreatureInspectSystem (тот тоже без прямой
    /// ссылки на UI): BattlePanel сам подписан (см. BattlePanel.OnCardDetail) и вызовет Show/Hide на своём
    /// _cardDetailPopupView независимо от того, откуда пришло событие.
    /// </summary>
    public class AuraStatusSlotView : CardBaseView
    {
        [SerializeField] TextMeshProUGUI _turnsText;
        [Tooltip("Плашка стака одноимённых чар («×2»/«×3»); скрыта при стаке 1. Опциональна.")]
        [SerializeField] TextMeshProUGUI _stackText;

        CardVisualData _visual;
        bool _hasData;

        public override void Unject()     { }
        public override void OnUse()      { }
        public override void OnClick()    => ConsumeHoldClick();   // тап по миниатюре — ничего, просто гасим hold-click
        public override void UpdateView() { }

        /// <summary>Заполнить слот данными активной ауры. turnsRemaining &lt; 0 — чара постоянная (без
        /// CharmTimerComponent) → показываем «∞» вместо числа. stackCount &gt; 1 — плашка «×N»
        /// (одноимённые чары схлопнуты в один слот, таймер — ближайший к истечению из стака).</summary>
        public void Setup(in CardVisualData visual, int turnsRemaining, int stackCount = 1)
        {
            _visual = visual;
            _hasData = true;
            ApplyVisualData(visual);
            if (_turnsText != null) _turnsText.text = turnsRemaining >= 0 ? turnsRemaining.ToString() : "∞";
            if (_stackText != null)
            {
                _stackText.gameObject.SetActive(stackCount > 1);
                if (stackCount > 1) _stackText.text = $"×{stackCount}";
            }
            gameObject.SetActive(true);
        }

        /// <summary>Пустой слот (аур меньше, чем слотов в баре).</summary>
        public void Clear()
        {
            _hasData = false;
            gameObject.SetActive(false);
        }

        protected override void OnHoldTriggered()
        {
            if (_hasData) GameEventBus.Publish(new CardDetailUIEvent { Visual = _visual, Show = true });
        }

        protected override void OnHoldReleased()
            => GameEventBus.Publish(new CardDetailUIEvent { Show = false });
    }
}
