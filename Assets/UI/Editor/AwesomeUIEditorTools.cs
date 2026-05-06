#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text;

namespace AwesomeUI.Editor
{
    public static class AwesomeUIEditorTools
    {
        private const string GENERATED_PATH = "Assets/UI/Generated";
        private const string CANVAS_TEMPLATE = @"using AwesomeUI.Core.Canvas;

namespace {0}
{{
    public class {1} : SourceCanvas
    {{
        public override void Init()
        {{
            base.Init();
            // Инициализация Canvas
        }}

        public override void OnInject()
        {{
            base.OnInject();
            // Вызывается после инъекции зависимостей
        }}
    }}
}}
";

        private const string PANEL_TEMPLATE = @"using AwesomeUI.Core.Panel;
using AwesomeUI.Core.Attributes;

namespace {0}
{{
    public class {1} : SourcePanel
    {{
        // [UIInject] private SomeService _service;

        public override void Init()
        {{
            base.Init();
            // Инициализация Panel
        }}

        public override void OnInject()
        {{
            base.OnInject();
            // Вызывается после инъекции зависимостей
        }}

        public override void Unject()
        {{
            // Очистка инъекций
        }}
    }}
}}
";

        private const string LAYOUT_TEMPLATE = @"using AwesomeUI.Core.Layout;
using AwesomeUI.Core.Attributes;

namespace {0}
{{
    public class {1} : SourceLayout
    {{
        // [UIInject] private SomeService _service;

        public override SourceLayout Init()
        {{
            base.Init();
            // Инициализация Layout
            return this;
        }}

        public override void OnInject()
        {{
            base.OnInject();
            // Вызывается после инъекции зависимостей
        }}

        public override void Unject()
        {{
            // Очистка инъекций
        }}
    }}
}}
";

        private const string WINDOW_TEMPLATE = @"using AwesomeUI.Core.Window;
using AwesomeUI.Core.Attributes;

namespace {0}
{{
    public class {1} : SourceWindow
    {{
        // [UIInject] private SomeService _service;

        public override SourceWindow Init()
        {{
            base.Init();
            // Инициализация Window
            return this;
        }}

        public override void OnInject()
        {{
            base.OnInject();
            // Вызывается после инъекции зависимостей
        }}

        public override void Unject()
        {{
            // Очистка инъекций
        }}
    }}
}}
";

        private const string SLOT_TEMPLATE = @"using AwesomeUI.Core.Slot;
using AwesomeUI.Core.Attributes;

namespace {0}
{{
    public class {1} : SourceSlot
    {{
        // [UIInject] private SomeService _service;

        public override SourceSlot Init()
        {{
            base.Init();
            // Инициализация Slot
            return this;
        }}

        public override void OnInject()
        {{
            base.OnInject();
        }}

        public override void Unject()
        {{
            // Очистка инъекций
        }}

        public override void OnActive()
        {{
            // Вызывается при активации слота
        }}

        public override void OnClick()
        {{
            // Вызывается при клике на слот
        }}

        public override void UpdateView()
        {{
            // Обновление визуального состояния
        }}
    }}
}}
";

        #region Menu Items

        [MenuItem("Assets/Create/AwesomeUI/Canvas", false, 80)]
        public static void CreateCanvas()
        {
            CreateUIElement("Canvas", CANVAS_TEMPLATE, "NewCanvas");
        }

        [MenuItem("Assets/Create/AwesomeUI/Panel", false, 81)]
        public static void CreatePanel()
        {
            CreateUIElement("Panel", PANEL_TEMPLATE, "NewPanel");
        }

        [MenuItem("Assets/Create/AwesomeUI/Layout", false, 82)]
        public static void CreateLayout()
        {
            CreateUIElement("Layout", LAYOUT_TEMPLATE, "NewLayout");
        }

        [MenuItem("Assets/Create/AwesomeUI/Window", false, 83)]
        public static void CreateWindow()
        {
            CreateUIElement("Window", WINDOW_TEMPLATE, "NewWindow");
        }

        [MenuItem("Assets/Create/AwesomeUI/Slot", false, 84)]
        public static void CreateSlot()
        {
            CreateUIElement("Slot", SLOT_TEMPLATE, "NewSlot");
        }

        [MenuItem("GameObject/AwesomeUI/Canvas", false, 10)]
        public static void CreateCanvasGameObject()
        {
            CreateUIGameObject<Core.Canvas.SourceCanvas>("Canvas");
        }

        [MenuItem("GameObject/AwesomeUI/Panel", false, 11)]
        public static void CreatePanelGameObject()
        {
            CreateUIGameObject<Core.Panel.SourcePanel>("Panel");
        }

        [MenuItem("GameObject/AwesomeUI/Layout", false, 12)]
        public static void CreateLayoutGameObject()
        {
            CreateUIGameObject<Core.Layout.SourceLayout>("Layout");
        }

        #endregion

        private static void CreateUIElement(string elementType, string template, string defaultName)
        {
            var window = EditorWindow.GetWindow<CreateUIElementWindow>(true, $"Create {elementType}");
            window.Setup(elementType, template, defaultName);
            window.ShowModal();
        }

        private static void CreateUIGameObject<T>(string name) where T : Component
        {
            var parent = Selection.activeTransform;
            var go = new GameObject(name);

            if (parent != null)
                go.transform.SetParent(parent, false);

            // Добавляем RectTransform для UI
            go.AddComponent<RectTransform>();

            Selection.activeGameObject = go;
            Undo.RegisterCreatedObjectUndo(go, $"Create {name}");
        }

        public static void GenerateScript(string className, string template, string folder, string namespaceName)
        {
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            string content = string.Format(template, namespaceName, className);
            string path = Path.Combine(folder, $"{className}.cs");

            if (File.Exists(path))
            {
                if (!EditorUtility.DisplayDialog("File Exists",
                    $"File {className}.cs already exists. Overwrite?", "Yes", "No"))
                    return;
            }

            File.WriteAllText(path, content, Encoding.UTF8);
            AssetDatabase.Refresh();

            var asset = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            if (asset != null)
            {
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
            }

            Debug.Log($"[AwesomeUI] Created: {path}");
        }
    }

    /// <summary>
    /// Окно создания UI элемента
    /// </summary>
    public class CreateUIElementWindow : EditorWindow
    {
        private string _elementType;
        private string _template;
        private string _className;
        private string _namespace = "AwesomeUI.Feature";
        private string _folder;

        public void Setup(string elementType, string template, string defaultName)
        {
            _elementType = elementType;
            _template = template;
            _className = defaultName;
            _folder = GetSelectedFolder();

            minSize = new Vector2(400, 150);
            maxSize = new Vector2(400, 150);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(10);

            EditorGUILayout.LabelField($"Create New {_elementType}", EditorStyles.boldLabel);

            EditorGUILayout.Space(5);

            _className = EditorGUILayout.TextField("Class Name", _className);
            _namespace = EditorGUILayout.TextField("Namespace", _namespace);
            _folder = EditorGUILayout.TextField("Folder", _folder);

            EditorGUILayout.Space(10);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Create", GUILayout.Width(100)))
            {
                if (ValidateInput())
                {
                    AwesomeUIEditorTools.GenerateScript(_className, _template, _folder, _namespace);
                    Close();
                }
            }

            if (GUILayout.Button("Cancel", GUILayout.Width(100)))
            {
                Close();
            }

            EditorGUILayout.EndHorizontal();
        }

        private bool ValidateInput()
        {
            if (string.IsNullOrWhiteSpace(_className))
            {
                EditorUtility.DisplayDialog("Error", "Class name cannot be empty!", "OK");
                return false;
            }

            if (!System.Text.RegularExpressions.Regex.IsMatch(_className, @"^[a-zA-Z_][a-zA-Z0-9_]*$"))
            {
                EditorUtility.DisplayDialog("Error", "Invalid class name!", "OK");
                return false;
            }

            return true;
        }

        private static string GetSelectedFolder()
        {
            var path = "Assets/UI/Generated";

            if (Selection.activeObject != null)
            {
                path = AssetDatabase.GetAssetPath(Selection.activeObject);
                if (!Directory.Exists(path))
                    path = Path.GetDirectoryName(path);
            }

            return path;
        }
    }
}
#endif