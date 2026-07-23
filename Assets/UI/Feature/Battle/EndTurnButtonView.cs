using Game.Core.Events;
using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Feature.Battle
{
    /// <summary>
    /// Кнопка «Конец хода» на BattlePanel — ручной аналог таймера: публикует RequestEndTurnUIEvent
    /// (его же шлёт дев-оверлей), дальше RunRequestEndTurnSystem → EndTurnRequestSystem ведут штатный
    /// каскад конца хода (OnTurnEnd-способности, оседание, передача хода).
    ///
    /// Активна ТОЛЬКО в свой ход: LocalTurnStartedEvent включает, OpponentTurnEndedEvent («ход ушёл
    /// оппоненту») и MatchEndedEvent выключают. После клика гасится сразу — от дабл-клика (система и так
    /// идемпотентна, но кнопка должна давать мгновенный отклик «запрос принят»).
    ///
    /// Жизненный цикл — как у TurnTimerView: НЕ SourceSlot, обычный MonoBehaviour; OnInject()/Unject()
    /// зовёт BattlePanel на каждый матч (канвас persistent — подписки в Awake/OnEnable пережили бы
    /// GameEventBus.Clear() между матчами мёртвыми).
    /// </summary>
    public class EndTurnButtonView : MonoBehaviour
    {
        [SerializeField] private Button _button;

        public void OnInject()
        {
            if (_button == null) _button = GetComponent<Button>();

            GameEventBus.Subscribe<LocalTurnStartedEvent>(OnLocalTurnStarted);
            GameEventBus.Subscribe<OpponentTurnEndedEvent>(OnOpponentTurn);
            GameEventBus.Subscribe<MatchEndedEvent>(OnMatchEnded);
            if (_button != null)
            {
                _button.onClick.AddListener(OnClick);
                _button.interactable = false;   // до первого своего хода кнопка неактивна (и сброс между матчами)
            }
        }

        public void Unject()
        {
            GameEventBus.Unsubscribe<LocalTurnStartedEvent>(OnLocalTurnStarted);
            GameEventBus.Unsubscribe<OpponentTurnEndedEvent>(OnOpponentTurn);
            GameEventBus.Unsubscribe<MatchEndedEvent>(OnMatchEnded);
            if (_button != null) _button.onClick.RemoveListener(OnClick);
        }

        void OnClick()
        {
            if (_button != null) _button.interactable = false;   // мгновенный отклик + защита от дабл-клика
            GameEventBus.Publish(new RequestEndTurnUIEvent());
        }

        void OnLocalTurnStarted(LocalTurnStartedEvent _)  { if (_button != null) _button.interactable = true; }
        void OnOpponentTurn(OpponentTurnEndedEvent _)     { if (_button != null) _button.interactable = false; }
        void OnMatchEnded(MatchEndedEvent _)              { if (_button != null) _button.interactable = false; }
    }
}
