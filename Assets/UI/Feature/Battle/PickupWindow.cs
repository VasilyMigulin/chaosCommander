using AwesomeUI.Core.Window;
using DG.Tweening;
using Game.Core.Events;
using Game.Core.Shared;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Feature.Battle
{
    /// <summary>
    /// Окно раскопки (discover / excavate).
    /// Открывается по CardPickOfferedEvent, показывает предложенные карты.
    /// Клик по карте → CardPickChosenEvent (выбор), кнопка отмены → CardPickCancelledEvent.
    /// Логику разрешения выбора (в т.ч. сетевую репликацию) ведёт CardPickSelectionSystem.
    ///
    /// Окно — дочернее у BattlePanel; SourcePanel.Init() авто-вызывает Init()/Dispose(),
    /// поэтому подписки живут здесь и отдельной регистрации в BattlePanel не требуется.
    /// </summary>
    public class PickupWindow : SourceWindow
    {
        [Header("Cards")]
        [SerializeField] private List<CardPickupView> _cardViews = new List<CardPickupView>();

        [Header("UI")]
        [SerializeField] private Button      _cancelButton;
        [SerializeField] private CanvasGroup _canvasGroup;

        // Токен показанного запроса — возвращается эхом в Chosen/Cancelled. Окно единственное, и слот в нём
        // арбитрирует CardPickBrokerSystem, так что перезаписи «на лету» больше нет: оффер приходит только
        // тогда, когда предыдущий уже разрешён.
        private int _requestId = 0;
        private int _castingCardEntity = -1;

        public override SourceWindow Init()
        {
            base.Init();
            _cardViews.ForEach(v => v.Init());
            if (_canvasGroup == null) _canvasGroup = GetComponent<CanvasGroup>();

            if (_cancelButton != null) _cancelButton.onClick.AddListener(OnCancel);

            gameObject.SetActive(false);
            return this;
        }

        public override void Dispose()
        {
            base.Dispose();
            if (_cancelButton != null) _cancelButton.onClick.RemoveListener(OnCancel);
        }

        // BattleCanvas — persistent (DontDestroyOnLoad): между матчами объект не пересоздаётся, Init()/Dispose()
        // второй раз не вызываются. А GameEventBus.Clear() (см. EcsRunHandler.Dispose при выходе из боя) обнуляет
        // ВСЮ шину — подписка из Init() пропадала бы навсегда после первого же матча (окно раскопки просто
        // переставало бы открываться). OnInject()/Unject() вызывает UIHandler НА КАЖДЫЙ матч.
        public override void OnInject()
        {
            GameEventBus.Subscribe<CardPickOfferedEvent>(OnOffered);
            GameEventBus.Subscribe<CardPickExpiredEvent>(OnExpired);
        }

        public override void Unject()
        {
            GameEventBus.Unsubscribe<CardPickOfferedEvent>(OnOffered);
            GameEventBus.Unsubscribe<CardPickExpiredEvent>(OnExpired);
        }

        // ── Events ───────────────────────────────────────────────────────────────

        private void OnOffered(CardPickOfferedEvent evt)
        {
            _requestId         = evt.RequestId;
            _castingCardEntity = evt.CastingCardEntity;

            for (int i = 0; i < _cardViews.Count; i++)
            {
                bool hasCard = evt.OfferedCardEntities != null
                    && i < evt.OfferedCount
                    && i < evt.OfferedCardEntities.Length;

                if (hasCard)
                {
                    CardVisualData? visual = evt.OfferedCardVisuals != null && i < evt.OfferedCardVisuals.Length
                        ? evt.OfferedCardVisuals[i]
                        : (CardVisualData?)null;

                    _cardViews[i].Setup(evt.OfferedCardEntities[i], visual, OnCardPicked);
                }
                else
                {
                    _cardViews[i].Clear();
                }
            }

            // Обязательный пик (замена добора) отменять нельзя: отменённый мандаторный запрос остался бы
            // висеть у продюсера и заглушил бы механику до конца матча.
            if (_cancelButton != null) _cancelButton.gameObject.SetActive(evt.AllowCancel);

            OnOpen();
        }

        // Ход закончился — окно не должно его пережить. Продюсер в этот же кадр свернёт запрос сам
        // (случайный выбор или отмена), UI просто закрывается и глушит свой токен, чтобы поздний клик
        // по уже разрешённому предложению не ушёл на шину.
        private void OnExpired(CardPickExpiredEvent evt)
        {
            if (_requestId == 0) return;
            _requestId = 0;
            OnClose();
        }

        private void OnCardPicked(CardPickupView view)
        {
            if (_requestId == 0) return;   // окна нет / уже разрешено — двойной клик не отправляем

            GameEventBus.Publish(new CardPickChosenEvent
            {
                RequestId         = _requestId,
                CastingCardEntity = _castingCardEntity,
                ChosenCardEntity  = view.CardEntity,
            });

            _requestId = 0;
            OnClose();
        }

        private void OnCancel()
        {
            if (_requestId == 0) return;

            GameEventBus.Publish(new CardPickCancelledEvent
            {
                RequestId         = _requestId,
                CastingCardEntity = _castingCardEntity,
            });

            _requestId = 0;
            OnClose();
        }

        // ── Show / Hide ───────────────────────────────────────────────────────────

        public override void OnOpen()
        {
            gameObject.SetActive(true);

            if (_canvasGroup != null)
            {
                // Прибить ХВОСТ fade-out прошлого показа: два окна подряд («Приглашение» — своя рука →
                // рука оппонента) открываются быстрее 0.2с, и OnComplete недобитого твина закрытия
                // (SetActive(false)) прятал СВЕЖЕоткрытое окно — «открылось и сразу закрылось».
                _canvasGroup.DOKill();
                _canvasGroup.alpha = 0f;
                _canvasGroup.DOFade(1f, 0.3f);
            }
        }

        public override void OnClose()
        {
            if (_canvasGroup != null)
            {
                _canvasGroup.DOKill();   // симметрично: закрытие поверх недоигранного открытия
                _canvasGroup.DOFade(0f, 0.2f)
                    .OnComplete(() => gameObject.SetActive(false));
            }
            else
            {
                gameObject.SetActive(false);
            }
        }
    }
}
