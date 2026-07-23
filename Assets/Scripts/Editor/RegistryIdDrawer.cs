using System;
using System.Linq;
using AwesomeUI.Core.Panel;
using Game.Core.Shared;
using UnityEditor;
using UnityEngine;

namespace Game.Core.EditorTools
{
    /// <summary>Общий рисователь дропдаута id (категория + «+») для строкового SerializedProperty.</summary>
    static class RegistryDropdown
    {
        const float AddWidth = 24f, Gap = 2f;

        public static void Draw(Rect position, GUIContent label, SerializedProperty valueProp, string category, string emptyLabel)
        {
            var catalog = RegistryIdCatalogUtil.GetOrCreate();
            var ids = catalog != null ? catalog.GetIds(category) : null;

            position = EditorGUI.PrefixLabel(position, GUIUtility.GetControlID(FocusType.Passive), label);
            var ddRect  = new Rect(position.x, position.y, position.width - AddWidth - Gap, position.height);
            var addRect = new Rect(position.xMax - AddWidth, position.y, AddWidth, position.height);

            string current = valueProp.stringValue;
            string shown = string.IsNullOrEmpty(current) ? emptyLabel : current;

            if (EditorGUI.DropdownButton(ddRect, new GUIContent(shown, category), FocusType.Keyboard))
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent(emptyLabel), string.IsNullOrEmpty(current), () => Apply(valueProp, ""));
                if (ids != null && ids.Count > 0)
                {
                    menu.AddSeparator("");
                    foreach (var id in ids.Where(s => !string.IsNullOrEmpty(s)).Distinct().OrderBy(s => s))
                    {
                        string captured = id;
                        menu.AddItem(new GUIContent(id), id == current, () => Apply(valueProp, captured));
                    }
                }
                menu.DropDown(ddRect);
            }

            if (GUI.Button(addRect, "+"))
                PopupWindow.Show(addRect, new AddIdPopup(catalog, category, newId => Apply(valueProp, newId)));
        }

        static void Apply(SerializedProperty valueProp, string value)
        {
            valueProp.stringValue = value;
            valueProp.serializedObject.ApplyModifiedProperties();
        }
    }

    /// <summary>Дровер для RegistryId (struct): дропдаун id категории поля ([RegistryCategory], иначе General).</summary>
    [CustomPropertyDrawer(typeof(RegistryId))]
    public class RegistryIdDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var valueProp = property.FindPropertyRelative("_value");
            if (valueProp == null) { EditorGUI.PropertyField(position, property, label); return; }
            RegistryDropdown.Draw(position, label, valueProp, ResolveCategory(), "(нет)");
        }

        string ResolveCategory()
        {
            var attr = fieldInfo != null
                ? (RegistryCategoryAttribute)Attribute.GetCustomAttribute(fieldInfo, typeof(RegistryCategoryAttribute))
                : null;
            return attr != null && !string.IsNullOrEmpty(attr.Category) ? attr.Category : RegistryCategories.General;
        }
    }

    /// <summary>Дровер для [PanelId] на string-поле SourcePanel: дропдаун категории UI/Panel; пусто = «(= ИмяКласса)».</summary>
    [CustomPropertyDrawer(typeof(PanelIdAttribute))]
    public class PanelIdDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }
            var target = property.serializedObject.targetObject;
            string typeName = target != null ? target.GetType().Name : "";
            string emptyLabel = string.IsNullOrEmpty(typeName) ? "(нет)" : $"(= {typeName})";
            RegistryDropdown.Draw(position, label, property, RegistryCategories.Panel, emptyLabel);
        }
    }

    /// <summary>Поиск/создание единственного каталога id.</summary>
    static class RegistryIdCatalogUtil
    {
        const string DefaultPath = "Assets/RegistryIdCatalog.asset";

        public static RegistryIdCatalog GetOrCreate()
        {
            var guids = AssetDatabase.FindAssets("t:RegistryIdCatalog");
            if (guids.Length > 0)
                return AssetDatabase.LoadAssetAtPath<RegistryIdCatalog>(AssetDatabase.GUIDToAssetPath(guids[0]));

            var asset = ScriptableObject.CreateInstance<RegistryIdCatalog>();
            AssetDatabase.CreateAsset(asset, DefaultPath);
            AssetDatabase.SaveAssets();
            return asset;
        }
    }

    /// <summary>Мини-попап «добавить id» рядом с полем (в конкретную категорию).</summary>
    class AddIdPopup : PopupWindowContent
    {
        readonly RegistryIdCatalog _catalog;
        readonly string _category;
        readonly Action<string> _onAdd;
        string _text = "";
        bool _focused;

        public AddIdPopup(RegistryIdCatalog catalog, string category, Action<string> onAdd)
        {
            _catalog = catalog;
            _category = category;
            _onAdd = onAdd;
        }

        public override Vector2 GetWindowSize() => new Vector2(260, 78);

        public override void OnGUI(Rect rect)
        {
            GUILayout.Label($"Новый id · {_category}", EditorStyles.boldLabel);

            GUI.SetNextControlName("newIdField");
            _text = EditorGUILayout.TextField(_text);
            if (!_focused) { EditorGUI.FocusTextInControl("newIdField"); _focused = true; }

            using (new EditorGUILayout.HorizontalScope())
            {
                bool enter = Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return;
                if ((GUILayout.Button("Добавить") || enter) && !string.IsNullOrWhiteSpace(_text))
                {
                    string id = _text.Trim();
                    if (_catalog != null && _catalog.Add(_category, id))
                    {
                        EditorUtility.SetDirty(_catalog);
                        AssetDatabase.SaveAssets();
                    }
                    _onAdd?.Invoke(id);
                    editorWindow.Close();
                }
                if (GUILayout.Button("Отмена")) editorWindow.Close();
            }
        }
    }
}
