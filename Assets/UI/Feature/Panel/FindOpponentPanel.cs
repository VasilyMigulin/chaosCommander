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
        // Фразы держим в КОДЕ (static readonly), НЕ в SerializeField: сериализованный массив на префабе
        // перекрывал новые дефолты — в инспекторе застревал старый короткий список.
        // Каждая фраза = (ключ локализации, RU-фолбэк). EN-шутки в card_text.csv — это АДАПТАЦИЯ, а не
        // перевод: часть русских острот в лоб не работает (см. ui.mm.flavor.*).
        static readonly (string Key, string Ru)[] SearchingPhrases =
        {
            ("ui.mm.flavor.01", "Ищем с кем подраться…"),
            ("ui.mm.flavor.02", "Ищем лоха…"),
            ("ui.mm.flavor.03", "Забиваем стрелку…"),
            ("ui.mm.flavor.04", "Готовим кулаки…"),
            ("ui.mm.flavor.05", "Заряжаемся на позитив…"),
            ("ui.mm.flavor.06", "Сканируем сервера на наличие жертв…"),
            ("ui.mm.flavor.07", "Зависли в ожидании…"),
            ("ui.mm.flavor.08", "Подбираем соперника по интеллекту… снижаем планку"),
            ("ui.mm.flavor.09", "Ищем достойного… ну или хоть кого-нибудь"),
            ("ui.mm.flavor.10", "Ждём смельчака…"),
            ("ui.mm.flavor.11", "Прочёсываем подворотни сервера…"),
            ("ui.mm.flavor.12", "Точим когти…"),
            ("ui.mm.flavor.13", "Разминаем костяшки…"),
            ("ui.mm.flavor.14", "Пакуем понты…"),
            ("ui.mm.flavor.15", "Ищем соперника с амбициями и без шансов…"),
            ("ui.mm.flavor.16", "Готовим отмазки на случай проигрыша…"),
            ("ui.mm.flavor.17", "Ищем добровольца на роль проигравшего…"),
            ("ui.mm.flavor.18", "Кто-то же должен проиграть… и это не мы"),
            ("ui.mm.flavor.19", "Ищем того, кто не забоится…"),
            ("ui.mm.flavor.20", "Зовём на разборки…"),
            ("ui.mm.flavor.21", "Готовим арену к побоищу…"),
            ("ui.mm.flavor.22", "Ищем жертву по объявлению…"),
            ("ui.mm.flavor.23", "Ставим на кон твоё эго…"),
            ("ui.mm.flavor.24", "Прогреваем скамейку запасных лохов…"),
            ("ui.mm.flavor.25", "Проверяем сервер на наличие храбрецов…"),
            ("ui.mm.flavor.26", "Ищем оппонента… а вдруг повезёт не тебе"),
            ("ui.mm.flavor.27", "Собираем секундантов…"),
            ("ui.mm.flavor.28", "Договариваемся о правилах… которых не будет"),
            ("ui.mm.flavor.29", "Ищем того, кому не жалко нервов…"),
            ("ui.mm.flavor.30", "Ищем спарринг-грушу…"),
            ("ui.mm.flavor.31", "Подбираем клоуна для твоего цирка…"),
            ("ui.mm.flavor.32", "Заряжаем сарказм…"),
            ("ui.mm.flavor.33", "Ищем оппонента с завышенной самооценкой…"),
            ("ui.mm.flavor.34", "Ищем того, кто ещё верит в свою колоду…"),
            ("ui.mm.flavor.35", "Наливаем сопернику ложных надежд…"),
            ("ui.mm.flavor.36", "Готовим корону проигравшего…"),
            ("ui.mm.flavor.37", "Ищем, кого сегодня проучить…"),
            ("ui.mm.flavor.38", "Ищем героя дня… чтобы его уронить"),
            ("ui.mm.flavor.39", "Проверяем, кто сегодня без фарта…"),
            ("ui.mm.flavor.40", "Раздуваем щёки…"),
            ("ui.mm.flavor.41", "Ищем оппонента, пока не передумал…"),
            ("ui.mm.flavor.42", "Тралим сервер на слабаков…"),
            ("ui.mm.flavor.43", "Готовим место для твоего поражения…"),
            ("ui.mm.flavor.44", "Ищем, об кого потренироваться…"),
        };

        CanvasGroup _statusCg;
        int  _flavorIndex = -1;
        bool _searching;
        MatchmakingUiStatus _applied = (MatchmakingUiStatus)(-1);   // что уже показано (−1 = ничего)

        // Крутёж-стейт (Update)
        enum Phase { Hold, FadeOut, FadeIn }
        Phase _phase;
        float _timer;

        public override void OnInject()
        {
            base.OnInject();
            if (_cancelBtn != null) _cancelBtn.onClick.AddListener(OnCancel);
        }

        void OnEnable()
        {
            // Крутёж НЕ завязан на инжект-цикл: при загрузке LobbyScene UI переинжектится (Unject гасил
            // _searching, а обратно его никто не взводил) — и фразы замирали ровно в лобби, при живом поиске.
            // Теперь состояние сверяется с персистентным хабом в Update, а тут только поднимаем «с нуля».
            _statusCg = EnsureGroup(_statusText);
            _applied = (MatchmakingUiStatus)(-1);
            _searching = false;
        }

        void ApplyStatus(MatchmakingUiStatus status)
        {
            _applied = status;

            if (status == MatchmakingUiStatus.Searching)
            {
                // НЕ перезапускаем крутёж, если он уже идёт: Photon при поиске гоняет состояния по кругу
                // (SearchingSessions→Joining→WaitingForPlayers→ретрай…), все маппятся в Searching — иначе
                // фраза менялась бы на каждом событии, сбрасывая фейд.
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
            // Статус тянем из персистентного хаба сами (а не только по событию): подписка живёт в инжект-
            // цикле и рвётся при смене сцены, а поиск — нет. Поллинг раз в кадр дешевле, чем ловить это.
            var status = MatchmakingUiHub.Current;
            if (status != _applied) ApplyStatus(status);

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
            var phrase = SearchingPhrases[_flavorIndex];
            // Резолвим на КАЖДОЙ смене фразы → смена языка подхватится прямо во время поиска.
            _statusText.text = CardTextLocalization.GetText(phrase.Key, phrase.Ru);
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
            // Мгновенный отклик: сессия гасится асинхронно (EndSession + LoadScene) — статус ставим сразу,
            // чтобы фразы поиска не крутились, пока идёт завершение.
            SetStatusAlpha(1f);
            SetStatus(MatchmakingUiStatus.Cancelled);
            Debug.Log($"[FindOpponentPanel] Cancel: hubHandler={(MatchmakingUiHub.CancelHandler != null ? "есть" : "НЕТ")} status={MatchmakingUiHub.Current}");
            MatchmakingUiHub.Cancel();   // завершит сессию и вернёт в меню (персистентный обработчик, переживает лобби)
        }

        public override void Unject()
        {
            // _searching здесь НЕ гасим: панель переживает смену сцены (Menu→Lobby) и переинжект, а поиск
            // при этом продолжается — сброс флага и останавливал крутёж. Update сам сверится с хабом.
            if (_cancelBtn != null) _cancelBtn.onClick.RemoveListener(OnCancel);
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
