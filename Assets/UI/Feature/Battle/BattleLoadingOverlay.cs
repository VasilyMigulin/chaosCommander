using DG.Tweening;
using Game.Core.Events;
using TMPro;
using UnityEngine;

namespace AwesomeUI.Feature.Battle
{
    /// <summary>
    /// Тёмный непрозрачный оверлей поверх BattleCanvas: закрывает боевую сцену, пока идёт хендшейк, и
    /// продолжает «движуху» шутливыми фразами (как FindOpponentPanel) — чтобы поиск → загрузка → VS был
    /// бесшовным, без сырой боевой сцены.
    ///
    /// Скрывается по CommandersRevealedUIEvent (VS берёт крышку) или, фоллбэк, по MulliganPhaseBeginUIEvent.
    /// Крутёж фраз — в Update() вручную (без корутины/DOTween), т.к. загрузка сцены может глушить корутину/твин.
    /// Fade-out самого оверлея — одноразовый DOTween (после стабилизации сцены).
    ///
    /// В сцене: последний (верхний) ребёнок BattleCanvas, ТЁМНЫЙ НЕПРОЗРАЧНЫЙ Image на весь экран, GameObject
    /// активен (видимость — CanvasGroup). _flavorText — опционально, TMP для фраз загрузки.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class BattleLoadingOverlay : MonoBehaviour
    {
        [SerializeField] float _fadeDuration = 0.3f;

        [Header("Loading flavor (опц.)")]
        [SerializeField] TextMeshProUGUI _flavorText;
        [SerializeField] float _flavorInterval = 2.2f;
        [SerializeField] float _flavorFade = 0.3f;
        // В КОДЕ (static readonly), не SerializeField — иначе сериализованный массив на префабе перекрывает.
        static readonly string[] LoadingPhrases =
        {
            "Раздаём карты…",
            "Тасуем колоду…",
            "Готовим арену к побоищу…",
            "Расставляем фигуры…",
            "Знакомим командиров…",
            "Заряжаем чертей…",
            "Натравливаем соперника…",
            "Полируем троны…",
            "Строим доску… криво, но сойдёт",
            "Будим командиров…",
            "Раздаём тумаки заранее…",
            "Проверяем, кто тут главный…",
            "Раскладываем ловушки…",
            "Подкупаем рандом…",
            "Греем кости для броска…",
            "Настраиваем невезение оппонента…",
            "Расчехляем командиров…",
            "Раздуваем интригу…",
        };

        CanvasGroup _cg;
        CanvasGroup _flavorCg;
        int  _flavorIndex = -1;
        bool _hidden;

        enum Phase { Hold, FadeOut, FadeIn }
        Phase _phase;
        float _timer;

        void Awake()
        {
            _cg = GetComponent<CanvasGroup>();
            _cg.alpha = 1f;
            _cg.blocksRaycasts = true;
            _flavorCg = EnsureGroup(_flavorText);
            BeginFlavor();
        }

        void OnEnable()
        {
            // SubscribePersistent, не Subscribe: объект persistent (DontDestroyOnLoad), OnEnable отрабатывает
            // ОДИН раз за сессию — обычная подписка терялась бы навсегда после первого GameEventBus.Clear()
            // (см. EcsRunHandler.Dispose при выходе из боя), и оверлей переставал бы сам прятаться со 2-го матча.
            GameEventBus.SubscribePersistent<CommandersRevealedUIEvent>(OnCommandersRevealed);
            GameEventBus.SubscribePersistent<MulliganPhaseBeginUIEvent>(OnMulliganPhaseBegin);
            GameEventBus.SubscribePersistent<PreStartPhaseBeginUIEvent>(OnPreStartPhaseBegin);
        }

        void OnDisable()
        {
            GameEventBus.UnsubscribePersistent<CommandersRevealedUIEvent>(OnCommandersRevealed);
            GameEventBus.UnsubscribePersistent<MulliganPhaseBeginUIEvent>(OnMulliganPhaseBegin);
            GameEventBus.UnsubscribePersistent<PreStartPhaseBeginUIEvent>(OnPreStartPhaseBegin);
        }

        void OnCommandersRevealed(CommandersRevealedUIEvent _) => Hide();
        void OnMulliganPhaseBegin(MulliganPhaseBeginUIEvent _) => Hide();
        // Туториал: мулигана/VS нет — бой начинается сразу с пре-старта (рука в UI). В MP к этому
        // моменту оверлей уже скрыт (guard _hidden) — подписка безвредна.
        void OnPreStartPhaseBegin(PreStartPhaseBeginUIEvent _) => Hide();

        void Hide()
        {
            if (_hidden) return;
            _hidden = true;

            _cg.blocksRaycasts = false;
            _cg.DOKill();
            _cg.DOFade(0f, _fadeDuration).SetUpdate(true);
        }

        /// <summary>Показать оверлей заново для НОВОГО матча. BattleCanvas — persistent (DontDestroyOnLoad,
        /// см. UIModule/UIHandler.InvokeCanvas): объект не пересоздаётся между боями, Awake()/OnEnable() второй
        /// раз не вызываются, поэтому без явного вызова оверлей на повторном бою оставался бы невидимым (alpha=0
        /// с прошлого фейда, _hidden=true навсегда) — раскрытие сцены/handshake проходило бы «голым».
        /// Вызывается из BattleState при открытии BattleCanvas для нового матча.</summary>
        public void Show()
        {
            _hidden = false;
            _cg.DOKill();
            _cg.alpha = 1f;
            _cg.blocksRaycasts = true;
            BeginFlavor();
        }

        // ── Крутёж фраз через Update (без корутины/DOTween) ────────────────────

        void BeginFlavor()
        {
            if (_flavorText == null || LoadingPhrases == null || LoadingPhrases.Length == 0) return;
            ShowPhrase();
            if (_flavorCg != null) _flavorCg.alpha = 1f;
            _phase = Phase.Hold;
            _timer = 0f;
        }

        void Update()
        {
            if (_hidden || _flavorText == null || LoadingPhrases == null || LoadingPhrases.Length == 0)
                return;

            // Кап на dt: оверлей висит на самых дёрганых кадрах (хендшейк/инит ECS/спавны Fusion) — один
            // длинный кадр (>_flavorFade) съедал весь фейд за раз (текст «просто переключался»). С капом
            // фейд растянется на несколько кадров даже при лагах загрузки.
            _timer += Mathf.Min(Time.unscaledDeltaTime, 0.05f);

            switch (_phase)
            {
                case Phase.Hold:
                    if (_timer >= _flavorInterval) { _phase = Phase.FadeOut; _timer = 0f; }
                    break;

                case Phase.FadeOut:
                    SetFlavorAlpha(1f - Clamp01(_timer / _flavorFade));
                    if (_timer >= _flavorFade) { ShowPhrase(); _phase = Phase.FadeIn; _timer = 0f; }
                    break;

                case Phase.FadeIn:
                    SetFlavorAlpha(Clamp01(_timer / _flavorFade));
                    if (_timer >= _flavorFade) { _phase = Phase.Hold; _timer = 0f; }
                    break;
            }
        }

        void ShowPhrase()
        {
            _flavorIndex = NextPhraseIndex();
            _flavorText.text = LoadingPhrases[_flavorIndex];
        }

        int NextPhraseIndex()
        {
            if (LoadingPhrases.Length == 1) return 0;
            int i;
            do { i = Random.Range(0, LoadingPhrases.Length); } while (i == _flavorIndex);
            return i;
        }

        void SetFlavorAlpha(float a) { if (_flavorCg != null) _flavorCg.alpha = a; }
        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        static CanvasGroup EnsureGroup(Component c)
        {
            if (c == null) return null;
            var g = c.GetComponent<CanvasGroup>();
            if (g == null) g = c.gameObject.AddComponent<CanvasGroup>();
            return g;
        }
    }
}
