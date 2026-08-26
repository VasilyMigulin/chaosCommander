using DG.Tweening;
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
    /// ПИНГ «НЕЧЕМ ХОДИТЬ» (2026-08-26): TurnTimerSystem больше не завершает такой ход сам — вместо
    /// этого шлёт NoActionsAvailableUIEvent, и кнопка сама подсказывает игроку нажать: иконка меняется
    /// с обычной на «завершить ход» и плавно пульсирует (DOTween, бесконечный Yoyo-скейл), пока действия
    /// не появятся снова или игрок не нажмёт сам.
    ///
    /// Жизненный цикл — как у TurnTimerView: НЕ SourceSlot, обычный MonoBehaviour; OnInject()/Unject()
    /// зовёт BattlePanel на каждый матч (канвас persistent — подписки в Awake/OnEnable пережили бы
    /// GameEventBus.Clear() между матчами мёртвыми).
    /// </summary>
    public class EndTurnButtonView : MonoBehaviour
    {
        [SerializeField] private Button _button;

        [Header("No actions ping")]
        [SerializeField] private Image  _icon;
        [SerializeField] private Sprite _normalSprite;
        [SerializeField] private Sprite _endTurnSprite;
        [SerializeField] private float  _pulseScale    = 1.15f;
        [SerializeField] private float  _pulseDuration = 0.5f;

        Sequence _pulseSequence;
        bool _isPinging;

        public void OnInject()
        {
            if (_button == null) _button = GetComponent<Button>();

            GameEventBus.Subscribe<LocalTurnStartedEvent>(OnLocalTurnStarted);
            GameEventBus.Subscribe<OpponentTurnEndedEvent>(OnOpponentTurn);
            GameEventBus.Subscribe<MatchEndedEvent>(OnMatchEnded);
            GameEventBus.Subscribe<NoActionsAvailableUIEvent>(OnNoActionsAvailable);
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
            GameEventBus.Unsubscribe<NoActionsAvailableUIEvent>(OnNoActionsAvailable);
            if (_button != null) _button.onClick.RemoveListener(OnClick);
            StopPing();
        }

        void OnClick()
        {
            if (_button != null) _button.interactable = false;   // мгновенный отклик + защита от дабл-клика
            StopPing();
            GameEventBus.Publish(new RequestEndTurnUIEvent());
        }

        void OnLocalTurnStarted(LocalTurnStartedEvent _)  { if (_button != null) _button.interactable = true; }
        void OnOpponentTurn(OpponentTurnEndedEvent _)     { if (_button != null) _button.interactable = false; StopPing(); }
        void OnMatchEnded(MatchEndedEvent _)              { if (_button != null) _button.interactable = false; StopPing(); }

        void OnNoActionsAvailable(NoActionsAvailableUIEvent e)
        {
            if (e.NoActions) StartPing();
            else StopPing();
        }

        void StartPing()
        {
            if (_isPinging) return;
            _isPinging = true;

            if (_icon != null && _endTurnSprite != null) _icon.sprite = _endTurnSprite;

            _pulseSequence?.Kill();
            _pulseSequence = DOTween.Sequence();
            _pulseSequence.Append(transform.DOScale(_pulseScale, _pulseDuration).SetEase(Ease.InOutSine));
            _pulseSequence.Append(transform.DOScale(1f, _pulseDuration).SetEase(Ease.InOutSine));
            _pulseSequence.SetLoops(-1);
        }

        void StopPing()
        {
            if (!_isPinging) return;
            _isPinging = false;

            if (_icon != null && _normalSprite != null) _icon.sprite = _normalSprite;

            _pulseSequence?.Kill();
            _pulseSequence = null;
            transform.localScale = Vector3.one;
        }

        void OnDestroy() => _pulseSequence?.Kill();
    }
}
