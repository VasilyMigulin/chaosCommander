using System.Collections.Generic;
using Game.Core.Configs;
using UnityEditor;
using UnityEngine;

namespace Game.Core.EditorTools
{
    /// <summary>
    /// Окно редактирования <see cref="KeyRegistry"/>: выбираешь раздел (или создаёшь
    /// новый), добавляешь/удаляешь ключи. Раздел «Expansion» можно автособрать из
    /// всех ExpansionConfig в проекте.
    /// </summary>
    public sealed class KeyRegistryWindow : EditorWindow
    {
        KeyRegistry _registry;
        int _sectionIndex;
        string _newSection = "";
        string _newKey = "";
        Vector2 _scroll;

        [MenuItem("Tools/Chaos Commander/Реестр ключей")]
        public static void Open()
        {
            var window = GetWindow<KeyRegistryWindow>("Реестр ключей");
            window.minSize = new Vector2(340, 260);
            window._registry = KeyRegistryEditorUtil.GetOrCreate();
            window.Show();
        }

        void OnEnable()
        {
            if (_registry == null) _registry = KeyRegistryEditorUtil.Find();
        }

        void OnGUI()
        {
            if (_registry == null)
            {
                EditorGUILayout.HelpBox("Реестр ключей не найден.", MessageType.Info);
                if (GUILayout.Button("Создать реестр"))
                    _registry = KeyRegistryEditorUtil.GetOrCreate();
                return;
            }

            DrawSectionPicker();
            EditorGUILayout.Space();
            DrawKeys();
            EditorGUILayout.Space();
            DrawTools();
        }

        void DrawSectionPicker()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Раздел", EditorStyles.boldLabel);

                var names = new List<string>(_registry.SectionNames());
                if (names.Count == 0)
                {
                    EditorGUILayout.LabelField("Разделов пока нет — создай ниже.", EditorStyles.miniLabel);
                }
                else
                {
                    _sectionIndex = Mathf.Clamp(_sectionIndex, 0, names.Count - 1);
                    _sectionIndex = EditorGUILayout.Popup("Текущий", _sectionIndex, names.ToArray());
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    _newSection = EditorGUILayout.TextField("Новый раздел", _newSection);
                    using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_newSection)))
                    {
                        if (GUILayout.Button("Создать", GUILayout.Width(80)))
                        {
                            Undo.RecordObject(_registry, "Add Section");
                            _registry.GetOrCreateSection(_newSection.Trim());
                            _sectionIndex = _registry.Sections.Count - 1;
                            _newSection = "";
                            Save();
                        }
                    }
                }
            }
        }

        void DrawKeys()
        {
            var names = new List<string>(_registry.SectionNames());
            if (names.Count == 0) return;

            string section = names[Mathf.Clamp(_sectionIndex, 0, names.Count - 1)];
            var keys = _registry.GetKeys(section);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField($"Ключи раздела «{section}» ({keys.Count})", EditorStyles.boldLabel);

                _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MinHeight(80));
                if (keys.Count == 0)
                {
                    EditorGUILayout.LabelField("Пусто", EditorStyles.miniLabel);
                }
                else
                {
                    // Копия, чтобы безопасно удалять во время отрисовки.
                    foreach (var key in new List<string>(keys))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUILayout.SelectableLabel(key, GUILayout.Height(18));
                            if (GUILayout.Button("✕", GUILayout.Width(24)))
                            {
                                Undo.RecordObject(_registry, "Remove Key");
                                _registry.RemoveKey(section, key);
                                Save();
                            }
                        }
                    }
                }
                EditorGUILayout.EndScrollView();

                using (new EditorGUILayout.HorizontalScope())
                {
                    _newKey = EditorGUILayout.TextField("Новый ключ", _newKey);
                    using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_newKey)))
                    {
                        if (GUILayout.Button("Добавить", GUILayout.Width(80)))
                        {
                            Undo.RecordObject(_registry, "Add Key");
                            if (!_registry.AddKey(section, _newKey))
                                Debug.LogWarning($"[KeyRegistry] Ключ «{_newKey.Trim()}» уже есть в разделе «{section}».");
                            _newKey = "";
                            Save();
                        }
                    }
                }
            }
        }

        void DrawTools()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Инструменты", EditorStyles.boldLabel);
                if (GUILayout.Button("Автособрать раздел «Expansion» из ExpansionConfig"))
                    CollectExpansionIds();
                if (GUILayout.Button("Выделить ассет реестра"))
                {
                    Selection.activeObject = _registry;
                    EditorGUIUtility.PingObject(_registry);
                }
            }
        }

        void CollectExpansionIds()
        {
            int added = 0;
            Undo.RecordObject(_registry, "Collect Expansion Ids");
            foreach (var guid in AssetDatabase.FindAssets("t:ExpansionConfig"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var config = AssetDatabase.LoadAssetAtPath<ExpansionConfig>(path);
                if (config == null || string.IsNullOrWhiteSpace(config.ExpansionId)) continue;
                if (_registry.AddKey(KeyRegistry.SectionExpansion, config.ExpansionId)) added++;
            }
            _sectionIndex = Mathf.Max(0, _registry.SectionNames().IndexOf(KeyRegistry.SectionExpansion));
            Save();
            Debug.Log($"[KeyRegistry] Раздел «{KeyRegistry.SectionExpansion}»: добавлено {added} новых ключей.");
        }

        void Save()
        {
            EditorUtility.SetDirty(_registry);
            AssetDatabase.SaveAssetIfDirty(_registry);
        }
    }

    static class ListExtensions
    {
        public static int IndexOf(this IReadOnlyList<string> list, string value)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i] == value) return i;
            return -1;
        }
    }
}
