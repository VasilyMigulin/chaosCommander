using AwesomeUI.Core.Attributes;
using AwesomeUI.Core.Window;
using DG.Tweening;
using Game.Core.Configs;
using Game.Core.Events;
using UnityEngine;

namespace AwesomeUI.Feature.Battle
{
    /// <summary>
    /// VS-экран перед мулиганом: тёмный фон → мой командир влетает из левого-нижнего угла →
    /// командир оппонента из правого-верхнего → VS-иконка «пыхает» → всё вместе растворяется по альфе.
    ///
    /// Открывается по CommandersRevealedUIEvent, закрывается (растворение) по MulliganPhaseBeginUIEvent
    /// (после паузы показа, из RPC_StartGame). Командир оппонента резолвится по exp/id через CardConfig
    /// (в локальном ECS его ещё нет — карты приедут снапшотом после мулигана).
    ///
    /// Раскладка в префабе:
    ///   _background        — CanvasGroup на тёмном Image на весь экран
    ///   _localCommander    — StaticCardView, поставить в ЛЕВЫЙ-НИЖНИЙ угол (его финальное место)
    ///   _opponentCommander — StaticCardView, в ПРАВЫЙ-ВЕРХНИЙ угол
    ///   _vsIcon            — RectTransform иконки «VS» по центру
    ///   _canvasGroup       — на корне окна (общее растворение на выходе)
    /// </summary>
    public class CommandersVsWindow : SourceWindow
    {
        [UIInject] private CardConfig _cardConfig;

        [Header("Cards")]
        [SerializeField] private StaticCardView _localCommander;
        [SerializeField] private StaticCardView _opponentCommander;

        [Header("Intro")]
        [SerializeField] private CanvasGroup  _background;   // тёмный фон
        [SerializeField] private RectTransform _vsIcon;      // иконка VS
        [SerializeField] private CanvasGroup  _canvasGroup;  // корень окна (растворение на выходе)

        [Header("Tuning")]
        [Tooltip("Откуда влетает мой командир (смещение от его места). Левый-нижний угол = отрицательные X/Y.")]
        [SerializeField] private Vector2 _localEnterOffset    = new Vector2(-700f, -450f);
        [Tooltip("Откуда влетает командир оппонента. Правый-верхний = положительные X/Y.")]
        [SerializeField] private Vector2 _opponentEnterOffset = new Vector2( 700f,  450f);
        [SerializeField] private float _bgFadeDuration   = 0.30f;
        [SerializeField] private float _cardMoveDuration = 0.40f;
        [SerializeField] private float _vsPopDuration    = 0.35f;
        [SerializeField] private float _outroDuration    = 0.45f;

        RectTransform _localRt, _oppRt;
        CanvasGroup   _localCg, _oppCg, _vsCg;
        Vector2 _localHome, _oppHome;
        Sequence _intro;

        public override SourceWindow Init()
        {
            base.Init();
            if (_canvasGroup == null) _canvasGroup = GetComponent<CanvasGroup>();

            // Захватываем «домашние» позиции карт (их финальные места из префаба) и группы для альфы.
            _localRt = _localCommander != null ? _localCommander.GetComponent<RectTransform>() : null;
            _oppRt   = _opponentCommander != null ? _opponentCommander.GetComponent<RectTransform>() : null;
            if (_localRt != null) _localHome = _localRt.anchoredPosition;
            if (_oppRt   != null) _oppHome   = _oppRt.anchoredPosition;

            _localCg = EnsureGroup(_localCommander);
            _oppCg   = EnsureGroup(_opponentCommander);
            _vsCg    = _vsIcon != null ? EnsureGroup(_vsIcon) : null;

            gameObject.SetActive(false);
            return this;
        }

        public override void Dispose()
        {
            base.Dispose();
            _intro?.Kill();
        }

        // BattleCanvas — persistent (DontDestroyOnLoad): между матчами объект не пересоздаётся, Init()/Dispose()
        // второй раз не вызываются. А GameEventBus.Clear() (см. EcsRunHandler.Dispose при выходе из боя) обнуляет
        // ВСЮ шину — подписки из Init() пропадали бы навсегда после первого же матча (VS-экран просто
        // переставал бы появляться на повторных боях). OnInject()/Unject() вызывает UIHandler НА КАЖДЫЙ матч
        // (см. BattleState.Start → UIModule.Inject) — здесь подписки переживают любое число матчей подряд.
        public override void OnInject()
        {
            GameEventBus.Subscribe<CommandersRevealedUIEvent>(OnRevealed);
            GameEventBus.Subscribe<MulliganPhaseBeginUIEvent>(OnMulliganPhaseBegin);
        }

        public override void Unject()
        {
            GameEventBus.Unsubscribe<CommandersRevealedUIEvent>(OnRevealed);
            GameEventBus.Unsubscribe<MulliganPhaseBeginUIEvent>(OnMulliganPhaseBegin);
        }

        private void OnRevealed(CommandersRevealedUIEvent e)
        {
            _localCommander?.SetModel(_cardConfig?.Get(e.LocalExpansionId, e.LocalCardId)?.CardData, true);
            _opponentCommander?.SetModel(_cardConfig?.Get(e.OpponentExpansionId, e.OpponentCardId)?.CardData, true);
            OnOpen();
        }

        private void OnMulliganPhaseBegin(MulliganPhaseBeginUIEvent _) => OnClose();

        public override void OnOpen()
        {
            gameObject.SetActive(true);
            transform.SetAsLastSibling();   // поверх окна мулигана

            _intro?.Kill();
            _canvasGroup.DOKill();
            _canvasGroup.alpha = 1f;                // окно целиком видимо; внутренние элементы гонят свою альфу
            _canvasGroup.blocksRaycasts = true;

            // Фон СРАЗУ непрозрачный (alpha 1, без фейда) — иначе при передаче от BattleLoadingOverlay
            // (тот фейдится из 1) оба полупрозрачны в момент кроссфейда → проблёскивает боевая сцена.
            // Готовый тёмный фон стоит под уходящим оверлеем → бесшовно. Анимируются только карты и VS-иконка.
            if (_background != null) _background.alpha = 1f;
            if (_localRt != null) _localRt.anchoredPosition = _localHome + _localEnterOffset;
            if (_oppRt   != null) _oppRt.anchoredPosition   = _oppHome   + _opponentEnterOffset;
            if (_localCg != null) _localCg.alpha = 0f;
            if (_oppCg   != null) _oppCg.alpha   = 0f;
            if (_vsIcon  != null) _vsIcon.localScale = Vector3.one * 0.3f;
            if (_vsCg    != null) _vsCg.alpha = 0f;

            _intro = DOTween.Sequence().SetUpdate(true);

            // Мой командир — из левого-нижнего угла
            if (_localRt != null)
            {
                _intro.Append(_localRt.DOAnchorPos(_localHome, _cardMoveDuration).SetEase(Ease.OutBack));
                if (_localCg != null) _intro.Join(_localCg.DOFade(1f, _cardMoveDuration));
            }

            // 3) Командир оппонента — из правого-верхнего угла
            if (_oppRt != null)
            {
                _intro.Append(_oppRt.DOAnchorPos(_oppHome, _cardMoveDuration).SetEase(Ease.OutBack));
                if (_oppCg != null) _intro.Join(_oppCg.DOFade(1f, _cardMoveDuration));
            }

            // 4) VS-иконка — «пых»
            if (_vsIcon != null)
            {
                _intro.Append(_vsIcon.DOScale(1f, _vsPopDuration).SetEase(Ease.OutBack));
                if (_vsCg != null) _intro.Join(_vsCg.DOFade(1f, _vsPopDuration));
            }
        }

        public override void OnClose()
        {
            _intro?.Kill();
            _canvasGroup.DOKill();
            _canvasGroup.blocksRaycasts = false;
            // Всё вместе растворяется по альфе (общий CanvasGroup окна).
            _canvasGroup.DOFade(0f, _outroDuration).SetUpdate(true)
                .OnComplete(() => gameObject.SetActive(false));
        }

        static CanvasGroup EnsureGroup(Component c)
        {
            if (c == null) return null;
            var g = c.GetComponent<CanvasGroup>();
            if (g == null) g = c.gameObject.AddComponent<CanvasGroup>();
            return g;
        }
    }
}
