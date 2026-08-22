using System.Collections.Generic;
using Game.Core.Configs;
using Game.Core.Service;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace Game.Core.EditorTools
{
    /// <summary>
    /// Odin-drawer для [KeyDropdown]: строковое поле рисуется выпадающим списком
    /// ключей нужного раздела из <see cref="KeyRegistry"/>. Справа — кнопка «✎»,
    /// открывающая окно редактирования ключей.
    /// </summary>
    public sealed class KeyDropdownAttributeDrawer : OdinAttributeDrawer<KeyDropdownAttribute, string>
    {
        const float EditButtonWidth = 24f;

        protected override void DrawPropertyLayout(GUIContent label)
        {
            var registry = KeyRegistryEditorUtil.Find();
            var keys = registry != null ? registry.GetKeys(Attribute.Section) : null;

            Rect rect = EditorGUILayout.GetControlRect();
            if (label != null) rect = EditorGUI.PrefixLabel(rect, label);

            var dropRect = new Rect(rect.x, rect.y, rect.width - EditButtonWidth - 2f, rect.height);
            var editRect = new Rect(rect.xMax - EditButtonWidth, rect.y, EditButtonWidth, rect.height);

            // Опции: «<пусто>» + ключи раздела. Если текущее значение вне раздела —
            // показываем его отдельной пометкой, чтобы не затереть «чужой» ключ.
            var options = new List<string> { "<пусто>" };
            if (keys != null) options.AddRange(keys);

            string current = ValueEntry.SmartValue;
            int selected = 0;
            if (!string.IsNullOrEmpty(current))
            {
                int idx = options.IndexOf(current);
                if (idx >= 0)
                {
                    selected = idx;
                }
                else
                {
                    options.Add($"{current}  (нет в разделе «{Attribute.Section}»)");
                    selected = options.Count - 1;
                }
            }

            int newSelected = EditorGUI.Popup(dropRect, selected, options.ToArray());
            if (newSelected != selected)
            {
                if (newSelected == 0)
                    ValueEntry.SmartValue = string.Empty;
                else if (keys != null && newSelected - 1 < keys.Count)
                    ValueEntry.SmartValue = keys[newSelected - 1];
                // выбор пометки «нет в разделе» ничего не меняет — это просто индикатор
            }

            if (GUI.Button(editRect, new GUIContent("✎", "Редактировать ключи")))
                KeyRegistryWindow.Open();
        }
    }
}
