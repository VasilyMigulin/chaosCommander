#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.SceneManagement;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

// === Toolbar Toggle ===
[Overlay(typeof(UnityEditor.SceneView), "Init Scene Toggle")]
[Icon("d_PlayButton On")]
public class InitScenePlayOverlay : ToolbarOverlay
{
    public InitScenePlayOverlay() : base(InitScenePlayToggle.ID) { }
}

[EditorToolbarElement(ID, typeof(SceneView))]
public class InitScenePlayToggle : EditorToolbarToggle
{
    public const string ID = "Custom/InitScenePlayToggle";
    private const string PrefKey = "InitSceneAutoLoader.Enabled";

    public InitScenePlayToggle()
    {
        text = "InitScene";
        tooltip = "Always start Play Mode from InitScene";
        value = EditorPrefs.GetBool(PrefKey, false);

        this.RegisterValueChangedCallback(evt =>
        {
            EditorPrefs.SetBool(PrefKey, evt.newValue);
        });
    }
}

// === Auto Scene Loader ===
[InitializeOnLoad]
public static class InitSceneAutoLoader
{
    private const string ScenePathKey = "InitSceneAutoLoader.ScenePath";
    private const string DefaultInitSceneName = "InitScene";

    static InitSceneAutoLoader()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        bool enabled = EditorPrefs.GetBool(InitScenePlayToggle.ID.Replace("Custom/", ""), false)
                      || EditorPrefs.GetBool("InitSceneAutoLoader.Enabled", false); // fallback

        if (!enabled)
            return;

        if (state == PlayModeStateChange.ExitingEditMode)
        {
            string initScenePath = FindInitScenePath();
            if (string.IsNullOrEmpty(initScenePath))
            {
                Debug.LogWarning($"[InitSceneAutoLoader] Scene '{DefaultInitSceneName}' not found in build settings!");
                return;
            }

            // Сохраняем текущую сцену
            EditorPrefs.SetString(ScenePathKey, SceneManager.GetActiveScene().path);

            // Загружаем InitScene
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(initScenePath);
            else
                EditorApplication.isPlaying = false;
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            // Возвращаемся к предыдущей сцене
            string lastScene = EditorPrefs.GetString(ScenePathKey, "");
            if (!string.IsNullOrEmpty(lastScene))
                EditorSceneManager.OpenScene(lastScene);
        }
    }

    private static string FindInitScenePath()
    {
        foreach (var scene in EditorBuildSettings.scenes)
        {
            if (scene.path.EndsWith($"{DefaultInitSceneName}.unity"))
                return scene.path;
        }
        return null;
    }
}
#endif
