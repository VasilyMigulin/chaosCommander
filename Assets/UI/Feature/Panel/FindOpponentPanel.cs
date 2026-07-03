using AwesomeUI.Core.Panel;
using AwesomeUI.Core.Attributes;
using AwesomeUI.Interface;
using Game.Core.Shared;
using Game.Core.Shared.Interface;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// Панель поиска соперника. Пока идёт поиск — крутит шутливые фразы с кроссфейдом (для настроения),
    /// на «найден/ошибка/отмена» показывает обычный статус. Мост статуса — через IMenuStateContext
    /// (без завязки на сборку Photon). Cancel возвращает в меню.
    ///
    /// Крутёж фраз гоняется в Update() вручную (таймер + альфа), БЕЗ корутины и DOTween: UI в
    /// DontDestroyOnLoad не деактивируется при смене сцен (Menu→Lobby→Battle), а корутину/твин загрузка
    /// сцены могла глушить (текст «замирал»). Update работает, пока объект активен — от сцен не зависит.
    ///
    /// Поля инспектора: _statusText (TMP строки), _cancelBtn, SearchingPhrases (набор шуток).
    /// </summary>
    public class FindOpponentPanel : SourcePanel
    {
        [SerializeField] private TextMeshProUGUI _statusText;
        [SerializeField] private Button _cancelBtn;

        [Header("Search flavor")]
        [Tooltip("Секунды показа фразы до смены.")]
        [SerializeField] private float _flavorInterval = 2.5f;
        [Tooltip("Длительность fade при смене фразы.")]
        [SerializeField] private float _flavorFade = 0.3f;
        // Фразы поиска держим в КОДЕ (static readonly), НЕ в SerializeField: сериализованный массив на
        // префабе перекрывал новые дефолты — в инспекторе застревал старый короткий список.
        static readonly string[] SearchingPhrases =
        {
            "Ищем с кем подраться…",
            "Ищем лоха…",
            "Забиваем стрелку…",
            "Готовим кулаки…",
            "Заряжаемся на позитив…",
            "Сканируем сервера на наличие жертв…",
            "Зависли в ожидании…",
            "Подбираем соперника по интеллекту… снижаем планку",
            "Ищем достойного… ну или хоть кого-нибудь",
            "Ждём смельчака…",
            "Прочёсываем подворотни сервера…",
            "Точим когти…",
            "Разминаем костяшки…",
            "Пакуем понты…",
            "Ищем соперника с амбициями и без шансов…",
            "Готовим отмазки на случай проигрыша…",
            "Ищем добровольца на роль проигравшего…",
            "Кто-то же должен проиграть… и это не мы",
            "Ищем того, кто не забоится…",
            "Зовём на разборки…",
            "Готовим арену к побоищу…",
            "Ищем жертву по объявлению…",
            "Ставим на кон твоё эго…",
            "Прогреваем скамейку запасных лохов…",
            "Проверяем сервер на наличие храбрецов…",
            "Ищем оппонента… а вдруг повезёт не тебе",
            "Собираем секундантов…",
            "Договариваемся о правилах… которых не будет",
            "Ищем того, кому не жалко нервов…",
            "Ищем спарринг-грушу…",
            "Подбираем клоуна для твоего цирка…",
            "Заряжаем сарказм…",
            "Ищем оппонента с завышенной самооценкой…",
            "Ищем того, кто ещё верит в свою колоду…",
            "Наливаем сопернику ложных надежд…",
            "Готовим корону проигравшего…",
            "Ищем, кого сегодня проучить…",
            "Ищем героя дня… чтобы его уронить",
            "Проверяем, кто сегодня без фарта…",
            "Раздуваем щёки…",
            "Ищем оппонента, пока не передумал…",
            "Тралим сервер на слабаков…",
            "Готовим место для твоего поражения…",
            "Ищем, об кого потренироваться…",
        };

        CanvasGroup _statusCg;
        int  _flavorIndex = -1;
        bool _searching;

        // Крутёж-стейт (Update)
        enum Phase { Hold, FadeOut, FadeIn }
        Phase _phase;
        float _timer;

        public override void OnInject()
        {
            base.OnInject();

            _statusCg = EnsureGroup(_statusText);
            if (_cancelBtn != null) _cancelBtn.onClick.AddListener(OnCancel);
            MatchmakingUiHub.StatusChanged += OnStatusChanged;   // персистентный источник (переживает гибель MenuState)

            OnStatusChanged(MatchmakingUiHub.Current);
        }

        void OnEnable()  => Debug.Log("[Flavor] FindOpponentPanel OnEnable (активна)");
        void OnDisable() => Debug.Log("[Flavor] FindOpponentPanel OnDisable (деактивирована)");

        void OnStatusChanged(MatchmakingUiStatus status)
        {
            bool searching = status == MatchmakingUiStatus.Searching;

            if (searching)
            {
                // ВАЖНО: НЕ перезапускать крутёж, если уже идёт. Photon-матчмейкинг при поиске гоняет
                // состояния по кругу (SearchingSessions→Joining→WaitingForPlayers→ретрай…), каждое маппится
                // в Searching → BeginFlavor на каждом событии мгновенно менял фразу и сбрасывал фазу фейда —
                // текст «просто переключался» без анимации.
                if (!_searching) { _searching = true; BeginFlavor(); }
                return;
            }

            _searching = false;
            SetStatusAlpha(1f);
            SetStatus(status);
        }

        // ── Крутёж фраз через Update (без корутины/DOTween) ────────────────────

        void BeginFlavor()
        {
            if (SearchingPhrases == null || SearchingPhrases.Length == 0)
            {
                SetStatus(MatchmakingUiStatus.Searching);
                SetStatusAlpha(1f);
                return;
            }
            ShowPhrase();
            SetStatusAlpha(1f);
            _phase = Phase.Hold;
            _timer = 0f;
        }

        void Update()
        {
            if (!_searching || _statusText == null || SearchingPhrases == null || SearchingPhrases.Length == 0)
                return;

            // Кап на dt: во время поиска грузятся сцены/коннект Fusion — кадры дёргаются, и один длинный
            // кадр (>_flavorFade) съедал весь фейд за раз (текст «просто переключался»). С капом фейд
            // растянется на несколько кадров даже при лагах.
            _timer += Mathf.Min(Time.unscaledDeltaTime, 0.05f);

            switch (_phase)
            {
                case Phase.Hold:
                    if (_timer >= _flavorInterval) { _phase = Phase.FadeOut; _timer = 0f; }
                    break;

                case Phase.FadeOut:
                    SetStatusAlpha(1f - Clamp01(_timer / _flavorFade));
                    if (_timer >= _flavorFade)
                    {
                        ShowPhrase();               // сменить фразу на «дне» фейда
                        _phase = Phase.FadeIn; _timer = 0f;
                    }
                    break;

                case Phase.FadeIn:
                    SetStatusAlpha(Clamp01(_timer / _flavorFade));
                    if (_timer >= _flavorFade) { _phase = Phase.Hold; _timer = 0f; }
                    break;
            }
        }

        void ShowPhrase()
        {
            _flavorIndex = NextPhraseIndex();
            _statusText.text = SearchingPhrases[_flavorIndex];
        }

        int NextPhraseIndex()
        {
            if (SearchingPhrases.Length == 1) return 0;
            int i;
            do { i = Random.Range(0, SearchingPhrases.Length); } while (i == _flavorIndex);
            return i;
        }

        void SetStatusAlpha(float a) { if (_statusCg != null) _statusCg.alpha = a; }
        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        // ── Конкретный статус (не поиск) ───────────────────────────────────────

        void SetStatus(MatchmakingUiStatus status)
        {
            if (_statusText == null) return;

            string key, ru;
            switch (status)
            {
                case MatchmakingUiStatus.OpponentFound: key = "ui.matchmaking.found";     ru = "Соперник найден!";              break;
                case MatchmakingUiStatus.Loading:       key = "ui.matchmaking.loading";    ru = "Загрузка боя…";                 break;
                case MatchmakingUiStatus.Failed:        key = "ui.matchmaking.failed";     ru = "Не удалось найти соперника";    break;
                case MatchmakingUiStatus.Cancelled:     key = "ui.matchmaking.cancelled";  ru = "Поиск отменён";                 break;
                default:                                key = "ui.matchmaking.searching";  ru = "Поиск соперника…";              break;
            }
            _statusText.text = CardTextLocalization.GetText(key, ru);
        }

        void OnCancel()
        {
            _searching = false;
            MatchmakingUiHub.Cancel();   // завершит сессию и вернёт в меню (персистентный обработчик, переживает лобби)
        }

        public override void Unject()
        {
            _searching = false;
            if (_cancelBtn != null) _cancelBtn.onClick.RemoveListener(OnCancel);
            MatchmakingUiHub.StatusChanged -= OnStatusChanged;
        }

        public override void OnDipose()
        {
            Unject();
            base.OnDipose();
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
