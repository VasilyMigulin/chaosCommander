using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace AwesomeUI.Core.Card.EditorTools
{
    /// <summary>
    /// Инспектор LocalizedText: вместо ручного ввода ключа — выпадающий список с поиском и
    /// группировкой по префиксу (ui.menu / ui.battle / type / card …). Ключи берутся из
    /// Assets/Localization/card_text.csv (первая колонка). Под полем — превью RU/EN.
    /// Кнопка «↻» перечитывает CSV (после правок/добавления ключей).
    /// </summary>
    [CustomEditor(typeof(LocalizedText))]
    public class LocalizedTextEditor : Editor
    {
        const string CsvRelative = "Localization/card_text.csv";

        static string[] _keys;
        static Dictionary<string, (string ru, string en)> _rows;

        SerializedProperty _keyProp;

        void OnEnable() => _keyProp = serializedObject.FindProperty("_key");

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EnsureLoaded();

            // Прочие поля компонента (если появятся), кроме самого ключа и m_Script.
            DrawPropertiesExcluding(serializedObject, "_key", "m_Script");

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(_keyProp, new GUIContent("Key",
                "Ключ локализации. Нажми ▼ чтобы выбрать из списка."));

            if (GUILayout.Button("▼", GUILayout.Width(26)))
            {
                var rect = GUILayoutUtility.GetLastRect();
                ShowPicker(rect);
            }
            if (GUILayout.Button("↻", GUILayout.Width(24)))
                Reload();
            EditorGUILayout.EndHorizontal();

            DrawPreview(_keyProp.stringValue);

            serializedObject.ApplyModifiedProperties();
        }

        void DrawPreview(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                EditorGUILayout.HelpBox("Ключ не задан → показывается текст из самого TMP (фоллбэк).", MessageType.None);
                return;
            }
            if (_rows != null && _rows.TryGetValue(key, out var v))
            {
                EditorGUILayout.LabelField("RU", v.ru, EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField("EN", v.en, EditorStyles.wordWrappedMiniLabel);
            }
            else
            {
                EditorGUILayout.HelpBox($"Ключа «{key}» нет в card_text.csv. Добавь строку или выбери из списка (▼).", MessageType.Warning);
            }
        }

        void ShowPicker(Rect rect)
        {
            EnsureLoaded();
            var dd = new KeyDropdown(new AdvancedDropdownState(), _keys, key =>
            {
                _keyProp.stringValue = key;
                serializedObject.ApplyModifiedProperties();
            });
            dd.Show(rect);
        }

        // ── CSV ──
        static void EnsureLoaded()
        {
            if (_keys != null) return;
            Reload();
        }

        static void Reload()
        {
            var keys = new List<string>();
            var rows = new Dictionary<string, (string, string)>();

            string path = Path.Combine(Application.dataPath, CsvRelative);
            if (File.Exists(path))
            {
                var lines = File.ReadAllLines(path);
                for (int i = 1; i < lines.Length; i++)   // пропускаем заголовок
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;
                    var cols = SplitCsv(lines[i]);
                    if (cols.Count == 0 || string.IsNullOrEmpty(cols[0])) continue;
                    string key = cols[0];
                    string ru = cols.Count > 1 ? cols[1] : "";
                    string en = cols.Count > 2 ? cols[2] : "";
                    keys.Add(key);
                    rows[key] = (ru, en);
                }
            }
            keys.Sort(StringComparer.Ordinal);
            _keys = keys.ToArray();
            _rows = rows;
        }

        static List<string> SplitCsv(string line)
        {
            var res = new List<string>();
            var sb = new System.Text.StringBuilder();
            bool inQ = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQ)
                {
                    if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else if (c == '"') inQ = false;
                    else sb.Append(c);
                }
                else
                {
                    if (c == '"') inQ = true;
                    else if (c == ';') { res.Add(sb.ToString()); sb.Clear(); }
                    else sb.Append(c);
                }
            }
            res.Add(sb.ToString());
            return res;
        }
    }

    /// <summary>Дерево ключей с поиском (AdvancedDropdown), сгруппированное по первому сегменту до точки.</summary>
    class KeyDropdown : AdvancedDropdown
    {
        readonly string[] _keys;
        readonly Action<string> _onPick;

        public KeyDropdown(AdvancedDropdownState state, string[] keys, Action<string> onPick) : base(state)
        {
            _keys = keys;
            _onPick = onPick;
            minimumSize = new Vector2(280, 360);
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            var root = new AdvancedDropdownItem("Ключи локализации");
            var groups = new Dictionary<string, AdvancedDropdownItem>();

            foreach (var key in _keys)
            {
                int dot = key.IndexOf('.');
                string group = dot > 0 ? key.Substring(0, dot) : "(прочее)";
                if (!groups.TryGetValue(group, out var g))
                {
                    g = new AdvancedDropdownItem(group);
                    groups[group] = g;
                    root.AddChild(g);
                }
                // Имя листа = полный ключ → в ItemSelected получаем его напрямую.
                g.AddChild(new AdvancedDropdownItem(key));
            }
            return root;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            // AdvancedDropdown зовёт это только для листа (по группам проваливается внутрь),
            // а имя листа = полный ключ.
            if (item != null) _onPick?.Invoke(item.name);
        }
    }
}
