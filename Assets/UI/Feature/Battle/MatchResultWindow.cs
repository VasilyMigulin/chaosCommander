using AwesomeUI.Core.Window;
using DG.Tweening;
using Game.Core.Events;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Feature.Battle
{
    /// <summary>
    /// Поп-ап результата матча. Открывается по MatchEndedEvent (победа/поражение/ничья).
    /// Под каждый результат — свой визуал (фон + misc-иконка вроде короны/черепа + подпись + тинт),
    /// настраивается в инспекторе (см. ResultVisual). Контейнеры наград/MMR зарезервированы (пока пустые).
    ///
    /// Окно — дочернее у BattlePanel; SourcePanel.Init() авто-вызывает Init()/Dispose(),
    /// поэтому подписки живут здесь и отдельной регистрации в BattlePanel не требуется
    /// (тот же паттерн, что и PickupWindow).
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

        [Header("UI refs")]
        [SerializeField] private TextMeshProUGUI _resultText;
        [SerializeField] private Image           _backgroundImage;
        [SerializeField] private Image           _miscImage;
        [SerializeField] private Button          _exitButton;
        [SerializeField] private CanvasGroup     _canvasGroup;

        [Header("Result visuals")]
        [SerializeField] private ResultVisual _winVisual  = new ResultVisual { caption = "Победа",     backgroundTint = Color.white };
        [SerializeField] private ResultVisual _loseVisual = new ResultVisual { caption = "Поражение",  backgroundTint = Color.white };
        [SerializeField] private ResultVisual _drawVisual = new ResultVisual { caption = "Ничья",      backgroundTint = Color.white };

        [Header("Future content (rewards/MMR) — placeholders")]
        [SerializeField] private GameObject _rewardsRoot;
        [SerializeField] private GameObject _mmrRoot;

        public override SourceWindow Init()
        {
            base.Init();
            if (_canvasGroup == null) _canvasGroup = GetComponent<CanvasGroup>();

            GameEventBus.Subscribe<MatchEndedEvent>(OnMatchEnded);
            if (_exitButton != null) _exitButton.onClick.AddListener(OnExit);

            // Награды/MMR пока скрыты (наполним в следующих итерациях).
            if (_rewardsRoot != null) _rewardsRoot.SetActive(false);
            if (_mmrRoot != null)     _mmrRoot.SetActive(false);

            gameObject.SetActive(false);
            return this;
        }

        public override void Dispose()
        {
            base.Dispose();
            GameEventBus.Unsubscribe<MatchEndedEvent>(OnMatchEnded);
            if (_exitButton != null) _exitButton.onClick.RemoveListener(OnExit);
        }

        public override void Unject() { }

        private void OnMatchEnded(MatchEndedEvent evt)
        {
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

        private void OnExit()
        {
            // Навигация/teardown боя (дисконнект Photon + смена состояния) обрабатываются на уровне States.
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
