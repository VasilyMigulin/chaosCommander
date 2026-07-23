using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Game.Core.Mono
{
    /// <summary>
    /// Дев-оверлей: ловит все логи Unity и даёт кнопки для копирования их в системный буфер обмена
    /// (работает в Editor и на Android/BlueStacks через GUIUtility.systemCopyBuffer). Плюс тех-действия
    /// боя/онбординга (End Turn, Fill Res, ColorRule, PvE, 1stRun, Tut). Само-инициализируется —
    /// в сцену вешать не нужно.
    ///
    /// ВСЁ скрыто за ЕДИНОЙ кнопкой «DEV» (правый край экрана) — общий тумблер DebugFlags.DevOverlayOpen.
    /// Пока закрыто, на экране только эта кнопка (не залепляет боевой/меню интерфейс). Тот же тумблер
    /// открывает и DevCheatMenu (экономика) — одна кнопка на все дев-панели.
    ///
    /// Кнопки внутри: Copy (всё), Copy Err (Warning/Error/Exception), Clear, Show/Hide (панель логов),
    /// End Turn, Fill Res, ColorRule, Copy Filt, PvE, 1stRun ✗, Tut ✓.
    /// Отключить целиком: DISABLE_LOG_OVERLAY в Scripting Define Symbols или удалить файл.
    /// </summary>
    public sealed class LogCopyOverlay : MonoBehaviour
    {
        const int MaxEntries = 2000;

        struct Entry { public string Message; public string Stack; public LogType Type; }

        readonly List<Entry> _entries = new List<Entry>(MaxEntries + 8);
        readonly object _lock = new object();

        bool _showPanel;
        Vector2 _scroll;
        string _toast;
        float _toastUntil;
        string _filter = "[Replay]";   // подстрока для «Copy Filt» (теги: [Replay] [Collect] [Resolve] [PhotonRunHandler] [ERR])

        GUIStyle _btn, _label, _logStyle, _field;
        float _scale = 1f;

#if !DISABLE_LOG_OVERLAY
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            var go = new GameObject("[LogCopyOverlay]");
            DontDestroyOnLoad(go);
            go.AddComponent<LogCopyOverlay>();
        }
#endif

        void OnEnable()  => Application.logMessageReceivedThreaded += OnLog;
        void OnDisable() => Application.logMessageReceivedThreaded -= OnLog;

        void OnLog(string message, string stack, LogType type)
        {
            lock (_lock)
            {
                _entries.Add(new Entry { Message = message, Stack = stack, Type = type });
                if (_entries.Count > MaxEntries)
                    _entries.RemoveRange(0, _entries.Count - MaxEntries);
            }
        }

        // ── Clipboard ──────────────────────────────────────────────────────────

        void CopyToClipboard(bool problemsOnly, string filter = null)
        {
            bool hasFilter = !string.IsNullOrEmpty(filter);
            var sb = new StringBuilder(8192);
            int count = 0;
            lock (_lock)
            {
                foreach (var e in _entries)
                {
                    if (problemsOnly && e.Type == LogType.Log) continue;
                    if (hasFilter && (e.Message == null ||
                        e.Message.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0))
                        continue;

                    sb.Append(Prefix(e.Type)).Append(e.Message).Append('\n');
                    if (e.Type == LogType.Error || e.Type == LogType.Exception || e.Type == LogType.Assert)
                        if (!string.IsNullOrEmpty(e.Stack)) sb.Append(e.Stack).Append('\n');
                    count++;
                }
            }

            GUIUtility.systemCopyBuffer = sb.ToString();
            Toast($"Copied {count} logs{(problemsOnly ? " (problems)" : "")}{(hasFilter ? $" [{filter}]" : "")} ({sb.Length} chars)");
        }

        static string Prefix(LogType t) => t switch
        {
            LogType.Error     => "[ERR] ",
            LogType.Exception => "[EXC] ",
            LogType.Assert    => "[ASRT] ",
            LogType.Warning   => "[WARN] ",
            _                 => "[LOG] ",
        };

        void Toast(string msg) { _toast = msg; _toastUntil = Time.realtimeSinceStartup + 2.5f; }

        // ── GUI ────────────────────────────────────────────────────────────────

        void EnsureStyles()
        {
            // Крупный масштаб под высокий DPI телефона/эмулятора. Базовый пол повыше — на узком портрете
            // width/1000 давал scale=1 и кнопки/текст были мелкими.
            _scale = Mathf.Max(1.15f, Screen.width / 900f);

            if (_btn == null)
            {
                _btn = new GUIStyle(GUI.skin.button);
                _label = new GUIStyle(GUI.skin.label);
                _logStyle = new GUIStyle(GUI.skin.label) { richText = false, wordWrap = true };
                _field = new GUIStyle(GUI.skin.textField);
            }
            int fs = Mathf.RoundToInt(23 * _scale);   // крупнее — читаемо на телефоне
            _btn.fontSize = fs;
            _label.fontSize = fs;
            _field.fontSize = fs;
            _logStyle.fontSize = Mathf.RoundToInt(17 * _scale);
        }

        // ── Flow-layout для дев-кнопок: текут слева-направо и ПЕРЕНОСЯТСЯ на новую строку, если не влезают
        //    по ширине (на узком портрете раньше уезжали за край и были нечитаемы). ──
        float _flowX, _flowY, _flowH, _flowPad, _flowMaxW;
        void FlowBegin(float startY, float rowH, float pad)
        { _flowX = pad; _flowY = startY; _flowH = rowH; _flowPad = pad; _flowMaxW = Screen.width - pad; }
        void FlowWrap(float w) { if (_flowX > _flowPad && _flowX + w > _flowMaxW) { _flowX = _flowPad; _flowY += _flowH + _flowPad; } }
        bool FlowButton(string label, float w)
        { FlowWrap(w); bool hit = GUI.Button(new Rect(_flowX, _flowY, w, _flowH), label, _btn); _flowX += w + _flowPad; return hit; }
        Rect FlowField(float w)
        { FlowWrap(w); var r = new Rect(_flowX, _flowY, w, _flowH); _flowX += w + _flowPad; return r; }
        float FlowBottom() => _flowY + _flowH;

        void OnGUI()
        {
            // Дев-оверлей — только редактор/dev-билд/dev-аккаунт (isDev). Логи ловим всегда (OnLog), но
            // кнопки/панель в релизе обычному игроку не рисуем.
            if (!Game.Core.Service.DebugFlags.DevUiAllowed) return;

            EnsureStyles();

            // ДВА тумблера у ЛЕВОГО края, стопкой (на мобилке F2 нет). Раздельные — чтобы экономика и логи
            // не открывались вместе и не перекрывались; лог-кнопки скрыты, пока не жмёшь LOG (случайно
            // ColorRule/PvE не заденешь). Кнопки слева, окно экономики (DevCheatMenu) — справа, не мешают.
            //   DEV → DevCheatMenu (валюта/бустеры/карты), LOG → этот оверлей (логи + бой/онбординг).
            float tbw = 132 * _scale, tbh = 62 * _scale, tgp = 8 * _scale, tbx = 6 * _scale, tcy = Screen.height * 0.5f;
            if (GUI.Button(new Rect(tbx, tcy - tbh - tgp, tbw, tbh), Game.Core.Service.DebugFlags.DevOverlayOpen ? "✕ DEV" : "DEV", _btn))
                Game.Core.Service.DebugFlags.DevOverlayOpen = !Game.Core.Service.DebugFlags.DevOverlayOpen;
            if (GUI.Button(new Rect(tbx, tcy + tgp, tbw, tbh), Game.Core.Service.DebugFlags.LogOverlayOpen ? "✕ LOG" : "LOG", _btn))
                Game.Core.Service.DebugFlags.LogOverlayOpen = !Game.Core.Service.DebugFlags.LogOverlayOpen;

            if (!Game.Core.Service.DebugFlags.LogOverlayOpen) return;

            float h = 52 * _scale;   // крупнее — палец и читаемость на телефоне
            float pad = 8 * _scale;

            int total, problems;
            lock (_lock)
            {
                total = _entries.Count;
                problems = 0;
                foreach (var e in _entries) if (e.Type != LogType.Log) problems++;
            }

            FlowBegin(pad, h, pad);

            if (FlowButton($"Copy ({total})",        200 * _scale)) CopyToClipboard(false);
            if (FlowButton($"Copy Err ({problems})", 220 * _scale)) CopyToClipboard(true);
            if (FlowButton("Clear",                  130 * _scale)) { lock (_lock) _entries.Clear(); Toast("Cleared"); }
            if (FlowButton(_showPanel ? "Hide" : "Show", 150 * _scale)) _showPanel = !_showPanel;
            // Ручной конец хода (пока нет UI-кнопки): сработает только если локальный игрок активен.
            if (FlowButton("End Turn",               190 * _scale))
            {
                Game.Core.Events.GameEventBus.Publish(new Game.Core.Events.RequestEndTurnUIEvent());
                Toast("End Turn requested");
            }
            // Тех-чит: выдать локальному игроку максимум маны и золота.
            if (FlowButton("Fill Res",               180 * _scale))
            {
                Game.Core.Events.GameEventBus.Publish(new Game.Core.Events.DebugFillResourcesEvent());
                Toast("Mana/Gold maxed");
            }
            // Тех-режим сборки колоды: игнорировать правило цвета.
            bool ignoreColor = Game.Core.Service.DebugFlags.IgnoreDeckColorRule;
            if (FlowButton(ignoreColor ? "ColorRule: OFF" : "ColorRule: ON", 250 * _scale))
            {
                Game.Core.Service.DebugFlags.IgnoreDeckColorRule = !ignoreColor;
                Toast($"Deck color rule {(!ignoreColor ? "ignored" : "enforced")}");
            }
            // Фильтр-подстрока + копирование только совпавших строк.
            _filter = GUI.TextField(FlowField(300 * _scale), _filter ?? "", _field);
            if (FlowButton("Copy Filt",              170 * _scale)) CopyToClipboard(false, _filter);
            // PvE: бой против ИИ без сети (энкаунтер из Resources/{PveMode.EncounterPath}). Через шину →
            // MenuState.StartPveBattle: тот сперва гасит активный матчмейкинг. В бою события никто не слушает.
            if (FlowButton("PvE",                    130 * _scale))
            {
                Toast($"PvE: запрошен бой (энкаунтер '{Game.Core.Service.PveMode.EncounterPath}')");
                Game.Core.Events.GameEventBus.Publish(new Game.Core.Events.PveStartRequestedEvent());
            }
            // Сброс цикла первого захода (язык/туториал/стартовый набор) — для теста онбординга.
            if (FlowButton("1stRun ✗",               180 * _scale))
            {
                Game.Core.Service.FirstRunFlow.ResetAll();
                Toast("Флаги первого захода сброшены (язык/туториал/стартовый набор)");
            }
            // Пропуск туториала (дев): чтобы роутинг первого захода не уводил в TutorialScene.
            if (FlowButton("Tut ✓",                  150 * _scale))
            {
                Game.Core.Service.FirstRunFlow.TutorialDone = true;
                Toast("Туториал помечен пройденным");
            }

            float bottom = FlowBottom();

            // Тост-подтверждение.
            if (_toast != null && Time.realtimeSinceStartup < _toastUntil)
                GUI.Label(new Rect(pad, bottom + pad, Screen.width - pad * 2, h), _toast, _label);

            if (!_showPanel) return;

            // Панель с последними логами (read-only, скролл).
            float panelY = bottom + pad + h;
            float panelH = Screen.height - panelY - pad;
            float panelW = Screen.width - pad * 2;
            GUI.Box(new Rect(pad, panelY, panelW, panelH), GUIContent.none);

            var view = new StringBuilder(8192);
            lock (_lock)
            {
                int from = Mathf.Max(0, _entries.Count - 300); // последние 300 в панели
                for (int i = from; i < _entries.Count; i++)
                    view.Append(Prefix(_entries[i].Type)).Append(_entries[i].Message).Append('\n');
            }

            var content = new GUIContent(view.ToString());
            float contentH = _logStyle.CalcHeight(content, panelW - 20 * _scale);
            _scroll = GUI.BeginScrollView(
                new Rect(pad + 4, panelY + 4, panelW - 8, panelH - 8),
                _scroll,
                new Rect(0, 0, panelW - 20 * _scale, contentH));
            GUI.Label(new Rect(0, 0, panelW - 20 * _scale, contentH), content, _logStyle);
            GUI.EndScrollView();
        }
    }
}
