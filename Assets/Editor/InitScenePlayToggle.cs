#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

[InitializeOnLoad]
public static class StartSceneToolbar
{
    private const string PrefKey = "InitSceneAutoLoader.SelectedScenePath";
    private const string PrevKey = "InitSceneAutoLoader.PreviousScene";

    private static bool _hooked = false;
    private static Button _button;

    static StartSceneToolbar()
    {
        EditorApplication.update += TryHookToolbar;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private static void TryHookToolbar()
    {
        if (_hooked) { EditorApplication.update -= TryHookToolbar; return; }

        var toolbarType  = typeof(Editor).Assembly.GetType("UnityEditor.Toolbar");
        var guiViewType  = typeof(Editor).Assembly.GetType("UnityEditor.GUIView");
        if (toolbarType == null || guiViewType == null) return;

        var toolbars = Resources.FindObjectsOfTypeAll(toolbarType);
        if (toolbars == null || toolbars.Length == 0) return;

        // Unity 6: rootVisualElement берём через windowBackend.visualTree
        var backendProp = guiViewType.GetProperty(
            "windowBackend", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (backendProp == null) return;

        var backend = backendProp.GetValue(toolbars[0]);
        if (backend == null) return;

        var visualTreeProp = backend.GetType().GetProperty(
            "visualTree", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (visualTreeProp == null) return;

        var root = visualTreeProp.GetValue(backend) as VisualElement;
        if (root == null) return;

        var zone = root.Q("ToolbarZonePlayMode")
                ?? root.Q("ToolbarZoneRightAlign");
        if (zone == null) return;

        _button = new Button(ShowSceneMenu);
        _button.tooltip = "Стартовая сцена для Play Mode";
        _button.AddToClassList("unity-toolbar-button");
        _button.style.minWidth = 110;
        RefreshLabel();

        zone.Insert(0, _button);
        _hooked = true;
    }

    private static void RefreshLabel()
    {
        if (_button == null) return;
        string path = EditorPrefs.GetString(PrefKey, "");
        _button.text = string.IsNullOrEmpty(path)
            ? "[ Scene: None ]"
            : $"[ {Path.GetFileNameWithoutExtension(path)} ]";
    }

    private static void ShowSceneMenu()
    {
        var menu = new GenericMenu();
        string current = EditorPrefs.GetString(PrefKey, "");

        menu.AddItem(new GUIContent("None (обычный запуск)"), string.IsNullOrEmpty(current), () =>
        {
            EditorPrefs.SetString(PrefKey, "");
            RefreshLabel();
        });

        menu.AddSeparator("");

        var scenes = EditorBuildSettings.scenes;
        if (scenes.Length == 0)
        {
            menu.AddDisabledItem(new GUIContent("Нет сцен в Build Settings"));
        }
        else
        {
            foreach (var scene in scenes)
            {
                string path = scene.path;
                string name = Path.GetFileNameWithoutExtension(path);
                menu.AddItem(new GUIContent(name), path == current, () =>
                {
                    EditorPrefs.SetString(PrefKey, path);
                    RefreshLabel();
                });
            }
        }

        menu.ShowAsContext();
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        string startScene = EditorPrefs.GetString(PrefKey, "");
        if (string.IsNullOrEmpty(startScene)) return;

        if (state == PlayModeStateChange.ExitingEditMode)
        {
            if (!File.Exists(startScene))
            {
                Debug.LogWarning($"[StartScene] Файл сцены не найден: {startScene}");
                EditorApplication.isPlaying = false;
                return;
            }

            EditorPrefs.SetString(PrevKey, SceneManager.GetActiveScene().path);

            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(startScene);
            else
                EditorApplication.isPlaying = false;
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            string prev = EditorPrefs.GetString(PrevKey, "");
            if (!string.IsNullOrEmpty(prev) && File.Exists(prev))
                EditorSceneManager.OpenScene(prev);
        }
    }
}
#endif

