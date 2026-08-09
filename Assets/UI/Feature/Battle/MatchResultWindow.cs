using AwesomeUI.Core.Window;
using DG.Tweening;
using Game.Core.Events;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace AwesomeUI.Feature.Battle
{
    /// <summary>
    /// Поп-ап результата матча — СТАДИЙНЫЙ флоу:
    ///   1) Result — «Победа/Поражение/Ничья» (визуал по ResultVisual), кнопка «Далее»;
    ///   2) Rating — на том же окне блок MMR: спиннер «Считаем рейтинг…» → «+N MMR» по
    ///      RatingUpdatedEvent (таймаут RatingWaitSeconds → «Рейтинг обновится позже»). Шаг только
    ///      для рейтинговых матчей (не PvE и MatchIdentity собрана) — иначе пропускается;
    ///   3) «Далее» → MatchRewardsWindow (золото за матч + карты PvE + задачи). Если ссылка окна
    ///      наград не набита в префабе — фолбэк: сразу выход в меню (старое поведение).
    ///
    /// «Матч рейтинговый» определяется В МОМЕНТ MatchEndedEvent: обычные подписчики шины бегут
    /// РАНЬШЕ персистентных (GameEventBus.Publish), поэтому MatchIdentity ещё не почищена
    /// персистентным MatchReportService.
    ///
    /// BattleCanvas — persistent (DontDestroyOnLoad): Init() отрабатывает один раз за сессию, а
    /// GameEventBus.Clear() в конце боя снимает подписки → подписки И ПОЛНЫЙ сброс состояния стадий
    /// живут в OnInject()/Unject() (вызываются на каждый матч; та же грабля задокументирована в
    /// MuliganWindow).
    /// </summary>
    public class MatchResultWindow : SourceWindow
    {
        /// <summary>Набор визуала под один результат. Любое поле опционально (null-safe).</summary>
        [Serializable]
        public struct ResultVisual
        {
            public string caption;          // подпись («Победа» / «Поражение» / «Ничья»)
            public Sprite background;       // фон (жёлтый/тёмный/нейтральный имедж)
            [Tooltip("Тинт фона; используется только если задан спрайт фона. White = без изменения.")]
            public Color  backgroundTint;
            public Sprite misc;             // доп. иконка (корона/череп и т.п.); скрыта если null
            public Color  captionColor;     // цвет подписи (Clear → не трогаем)
        }

        const float RatingWaitSeconds = 8f;   // ждём дельту (джиттер проигравшего 2с + ретраи сервера)

        [Header("UI refs")]
        [SerializeField] private TextMeshProUGUI _resultText;
        [SerializeField] private Image           _backgroundImage;
        [SerializeField] private Image           _miscImage;
        [FormerlySerializedAs("_exitButton")]
        [SerializeField] private Button          _nextButton;
        [SerializeField] private CanvasGroup     _canvasGroup;

        [Header("Result visuals")]
        [SerializeField] private ResultVisual _winVisual  = new ResultVisual { caption = "Победа",     backgroundTint = Color.white };
        [SerializeField] private ResultVisual _loseVisual = new ResultVisual { caption = "Поражение",  backgroundTint = Color.white };
        [SerializeField] private ResultVisual _drawVisual = new ResultVisual { caption = "Ничья",      backgroundTint = Color.white };

        [Header("Стадия рейтинга (блок MMR на этом же окне)")]
        [SerializeField] private GameObject _mmrRoot;
        [SerializeField] private TextMeshProUGUI _mmrText;

        [Header("Стадия наград (соседнее окно в BattlePanel)")]
        [Tooltip("Не набито → кнопка Далее ведёт сразу в меню (старое поведение).")]
        [SerializeField] private MatchRewardsWindow _rewardsWindow;

        enum Stage { Result, Rating }

        Stage _stage;
        bool  _ratingExpected;   // матч рейтинговый → между Result и наградами есть шаг MMR
        bool  _ratingArrived;
        RatingUpdatedEvent _rating;
        Tween _ratingTimeout;

        public override SourceWindow Init()
        {
            base.Init();
            if (_canvasGroup == null) _canvasGroup = GetComponent<CanvasGroup>();
            if (_nextButton != null) _nextButton.onClick.AddListener(OnNext);
            if (_mmrRoot != null) _mmrRoot.SetActive(false);
            gameObject.SetActive(false);
            return this;
        }

        public override void Dispose()
        {
            base.Dispose();
            if (_nextButton != null) _nextButton.onClick.RemoveListener(OnNext);
            KillRatingTimeout();
        }

        public override void OnInject()
        {
            GameEventBus.Subscribe<MatchEndedEvent>(OnMatchEnded);
            GameEventBus.Subscribe<RatingUpdatedEvent>(OnRatingUpdated);

            // Полный сброс состояния ПРЕДЫДУЩЕГО матча (persistent-канвас, Init второй раз не придёт).
            _stage = Stage.Result;
            _ratingExpected = false;
            _ratingArrived = false;
            KillRatingTimeout();
            if (_mmrRoot != null) _mmrRoot.SetActive(false);
            _canvasGroup?.DOKill();
            gameObject.SetActive(false);
        }

        public override void Unject()
        {
            GameEventBus.Unsubscribe<MatchEndedEvent>(OnMatchEnded);
            GameEventBus.Unsubscribe<RatingUpdatedEvent>(OnRatingUpdated);
            KillRatingTimeout();
        }

        // ── Стадия 1: результат ──────────────────────────────────────────────────

        private void OnMatchEnded(MatchEndedEvent evt)
        {
            _stage = Stage.Result;
            _ratingArrived = false;

            // Обычный подписчик бежит раньше персистентного MatchReportService → identity ещё на месте.
            _ratingExpected = !Game.Core.Service.PveMode.Enabled && Game.Core.Service.MatchIdentity.IsSet;

            ResultVisual v = evt.LocalResult switch
            {
                MatchResult.Win  => _winVisual,
                MatchResult.Lose => _loseVisual,
                _                => _drawVisual,
            };

            // Локализуем подпись результата (фоллбэк — caption из инспектора).
            string key = evt.LocalResult switch
            {
                MatchResult.Win  => "ui.battle.result_win",
                MatchResult.Lose => "ui.battle.result_lose",
                _                => "ui.battle.result_draw",
            };
            v.caption = Game.Core.Shared.CardTextLocalization.GetText(key, v.caption);

            if (_mmrRoot != null) _mmrRoot.SetActive(false);   // MMR появится на своей стадии

            Apply(v);
            OnOpen();
        }

        private void Apply(ResultVisual v)
        {
            if (_resultText != null)
            {
                _resultText.text = v.caption;
                if (v.captionColor.a > 0f) _resultText.color = v.captionColor;
            }

            if (_backgroundImage != null && v.background != null)
            {
                _backgroundImage.sprite = v.background;
                _backgroundImage.color  = v.backgroundTint.a > 0f ? v.backgroundTint : Color.white;
            }

            if (_miscImage != null)
            {
                _miscImage.sprite = v.misc;
                _miscImage.gameObject.SetActive(v.misc != null);   // нет иконки → скрыть
            }
        }

        // ── Стадия 2: рейтинг ────────────────────────────────────────────────────

        private void OnRatingUpdated(RatingUpdatedEvent evt)
        {
            _ratingArrived = true;
            _rating = evt;
            if (_stage == Stage.Rating) ShowRatingValue();   // пришла, пока смотрим на спиннер
        }

        private void EnterRatingStage()
        {
            _stage = Stage.Rating;
            if (_mmrRoot != null) _mmrRoot.SetActive(true);

            if (_ratingArrived) { ShowRatingValue(); return; }

            // Спиннер: дельта обычно приезжает за 1-5с (джиттер проигравшего + ретраи). Кнопку не
            // блокируем — игрок вправе не ждать (расчёт применится на сервере и без показа).
            if (_mmrText != null)
                _mmrText.text = Game.Core.Shared.CardTextLocalization.GetText("ui.battle.mmr_waiting", "Считаем рейтинг…");

            KillRatingTimeout();
            _ratingTimeout = DOVirtual.DelayedCall(RatingWaitSeconds, () =>
            {
                if (_ratingArrived || _mmrText == null) return;
                _mmrText.text = Game.Core.Shared.CardTextLocalization.GetText("ui.battle.mmr_later", "Рейтинг обновится позже");
            });
        }

        private void ShowRatingValue()
        {
            KillRatingTimeout();
            if (_mmrText == null) return;

            string key = _rating.Delta >= 0 ? "ui.battle.mmr_gain" : "ui.battle.mmr_loss";
            string fmt = Game.Core.Shared.CardTextLocalization.GetText(key, _rating.Delta >= 0 ? "+{0} MMR" : "−{0} MMR");
            _mmrText.text = string.Format(fmt, Mathf.Abs(_rating.Delta));

            // Лёгкий акцент на появлении числа.
            _mmrText.transform.DOKill();
            _mmrText.transform.localScale = Vector3.one;
            _mmrText.transform.DOPunchScale(Vector3.one * 0.2f, 0.35f, vibrato: 4);
        }

        private void KillRatingTimeout()
        {
            _ratingTimeout?.Kill();
            _ratingTimeout = null;
        }

        // ── Переходы ─────────────────────────────────────────────────────────────

        private void OnNext()
        {
            if (_stage == Stage.Result && _ratingExpected)
            {
                EnterRatingStage();
                return;
            }
            OpenRewards();
        }

        private void OpenRewards()
        {
            KillRatingTimeout();

            if (_rewardsWindow != null)
            {
                _rewardsWindow.ShowAfterMatch();
                OnClose();
                return;
            }

            // Окно наград не набито в префабе — старое поведение: сразу в меню
            // (навигация/teardown боя обрабатываются на уровне States).
            GameEventBus.Publish(new ExitToMenuRequestedEvent());
        }

        // ── Show / Hide ───────────────────────────────────────────────────────────

        public override void OnOpen()
        {
            gameObject.SetActive(true);

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
                _canvasGroup.DOFade(1f, 0.3f);
            }
        }

        public override void OnClose()
        {
            if (_canvasGroup != null)
            {
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
