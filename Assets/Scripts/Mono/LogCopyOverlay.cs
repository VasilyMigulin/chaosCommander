using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Game.Core.Mono
{
    /// <summary>
    /// Дев-оверлей: ловит все логи Unity и даёт кнопки в ЛЕВОМ ВЕРХНЕМ углу для копирования их
    /// в системный буфер обмена (работает в Editor и на Android/BlueStacks через
    /// GUIUtility.systemCopyBuffer). Само-инициализируется — в сцену вешать не нужно.
    ///
    /// Кнопки: Copy (всё), Copy Err (только Warning/Error/Exception), Clear, Show/Hide (панель логов).
    /// Отключить: задать DISABLE_LOG_OVERLAY в Scripting Define Symbols или удалить файл.
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
            // Крупный масштаб под высокий DPI телефона/эмулятора.
            _scale = Mathf.Max(1f, Screen.width / 1000f);

            if (_btn == null)
            {
                _btn = new GUIStyle(GUI.skin.button);
                _label = new GUIStyle(GUI.skin.label);
                _logStyle = new GUIStyle(GUI.skin.label) { richText = false, wordWrap = true };
                _field = new GUIStyle(GUI.skin.textField);
            }
            int fs = Mathf.RoundToInt(16 * _scale);
            _btn.fontSize = fs;
            _label.fontSize = fs;
            _field.fontSize = fs;
            _logStyle.fontSize = Mathf.RoundToInt(13 * _scale);
        }

        void OnGUI()
        {
            EnsureStyles();

            float h = 40 * _scale;
            float pad = 6 * _scale;
            float x = pad, y = pad;

            int total, problems;
            lock (_lock)
            {
                total = _entries.Count;
                problems = 0;
                foreach (var e in _entries) if (e.Type != LogType.Log) problems++;
            }

            float bw = 130 * _scale;
            if (GUI.Button(new Rect(x, y, bw, h), $"Copy ({total})", _btn)) CopyToClipboard(false);
            x += bw + pad;
            if (GUI.Button(new Rect(x, y, bw, h), $"Copy Err ({problems})", _btn)) CopyToClipboard(true);
            x += bw + pad;
            if (GUI.Button(new Rect(x, y, 90 * _scale, h), "Clear", _btn))
            {
                lock (_lock) _entries.Clear();
                Toast("Cleared");
            }
            x += 90 * _scale + pad;
            if (GUI.Button(new Rect(x, y, 110 * _scale, h), _showPanel ? "Hide" : "Show", _btn))
                _showPanel = !_showPanel;
            x += 110 * _scale + pad;
            // Ручной конец хода (пока нет UI-кнопки): сработает только если локальный игрок активен.
            if (GUI.Button(new Rect(x, y, 150 * _scale, h), "End Turn", _btn))
            {
                Game.Core.Events.GameEventBus.Publish(new Game.Core.Events.RequestEndTurnUIEvent());
                Toast("End Turn requested");
            }
            x += 150 * _scale + pad;
            // Тех-чит: выдать локальному игроку максимум маны и золота.
            if (GUI.Button(new Rect(x, y, 150 * _scale, h), "Fill Res", _btn))
            {
                Game.Core.Events.GameEventBus.Publish(new Game.Core.Events.DebugFillResourcesEvent());
                Toast("Mana/Gold maxed");
            }
            x += 150 * _scale + pad;
            // Тех-режим сборки колоды: игнорировать правило цвета.
            bool ignoreColor = Game.Core.Service.DebugFlags.IgnoreDeckColorRule;
            if (GUI.Button(new Rect(x, y, 190 * _scale, h), ignoreColor ? "ColorRule: OFF" : "ColorRule: ON", _btn))
            {
                Game.Core.Service.DebugFlags.IgnoreDeckColorRule = !ignoreColor;
                Toast($"Deck color rule {(!ignoreColor ? "ignored" : "enforced")}");
            }
            // Второй ряд: фильтр-подстрока + копирование только совпавших строк + PvE.
            // NB: первый ряд занимает ~99% ширины экрана (масштаб = width/1000) — восьмая кнопка
            // уезжала за правый край и была невидима. Поэтому PvE живёт во ВТОРОМ ряду.
            float y2 = y + h + pad;
            _filter = GUI.TextField(new Rect(pad, y2, 260 * _scale, h), _filter ?? "", _field);
            if (GUI.Button(new Rect(pad + 260 * _scale + pad, y2, 150 * _scale, h), "Copy Filt", _btn))
                CopyToClipboard(false, _filter);
            // PvE: бой против ИИ без сети (энкаунтер из Resources/{PveMode.EncounterPath}).
            // Через шину → MenuState.StartPveBattle: тот сперва ЧИСТО гасит активный матчмейкинг
            // (LoadScene посреди джоина оставлял оверлей поиска поверх боя). В бою события никто
            // не слушает → кнопка там безвредна.
            if (GUI.Button(new Rect(pad + 260 * _scale + pad + 150 * _scale + pad, y2, 110 * _scale, h), "PvE", _btn))
            {
                Toast($"PvE: запрошен бой (энкаунтер '{Game.Core.Service.PveMode.EncounterPath}')");
                Game.Core.Events.GameEventBus.Publish(new Game.Core.Events.PveStartRequestedEvent());
            }
            // Сброс цикла первого захода (язык/туториал/стартовый набор) — для теста онбординга.
            if (GUI.Button(new Rect(pad + 260 * _scale + pad + 150 * _scale + pad + 110 * _scale + pad, y2, 150 * _scale, h), "1stRun ✗", _btn))
            {
                Game.Core.Service.FirstRunFlow.ResetAll();
                Toast("Флаги первого захода сброшены (язык/туториал/стартовый набор)");
            }
            // Пропуск туториала (дев): чтобы роутинг первого захода не уводил в TutorialScene.
            if (GUI.Button(new Rect(pad + 260 * _scale + pad + 150 * _scale + pad + 110 * _scale + pad + 150 * _scale + pad, y2, 110 * _scale, h), "Tut ✓", _btn))
            {
                Game.Core.Service.FirstRunFlow.TutorialDone = true;
                Toast("Туториал помечен пройденным");
            }

            // Тост-подтверждение.
            if (_toast != null && Time.realtimeSinceStartup < _toastUntil)
                GUI.Label(new Rect(pad, y2 + h + pad, Screen.width - pad * 2, h), _toast, _label);

            if (!_showPanel) return;

            // Панель с последними логами (read-only, скролл).
            float panelY = y2 + h + pad + h;
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
