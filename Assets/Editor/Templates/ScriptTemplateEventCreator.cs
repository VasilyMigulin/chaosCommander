using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text;
using System;
using UnityEditor.ProjectWindowCallback;

public sealed class ScriptTemplateEventCreator : ScriptableObject
{
    const string Title = "Event template generator";
    const string TargetPath = "Assets/Scripts/Events";
    const string Namespace = "Game.Core.Events";

    const string InterfaceTemplate = 
        "namespace " + Namespace + "\n" +
        "{\n" +
        "    public struct #NAME#Event : IGameEvent\n" +
        "    {\n" +
        "\n" +
        "    }\n" +
        "}\n";

    [MenuItem("Assets/Create/Scripting/Event (Game.Core.Events)", false, 80)]
    static void CreateInterface()
    {
        if (!AssetDatabase.IsValidFolder(TargetPath))
        {
            Directory.CreateDirectory(TargetPath);
            AssetDatabase.Refresh();
        }

        CreateAndRenameAsset($"{TargetPath}/PlayerEvent.cs", GetIcon(),
            (name) => CreateTemplateInternal(name));
    }

    static string CreateTemplateInternal(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            EditorUtility.DisplayDialog(Title, "Invalid filename", "Close");
            return "Invalid filename";
        }

        var rawName = SanitizeClassName(Path.GetFileNameWithoutExtension(fileName));

        // Strip existing "Event" suffix to avoid duplication
        if (rawName.EndsWith("Event", StringComparison.Ordinal))
            rawName = rawName.Substring(0, rawName.Length - "Event".Length);

        if (string.IsNullOrEmpty(rawName))
        {
            EditorUtility.DisplayDialog(Title, "Invalid filename", "Close");
            return "Invalid filename";
        }

        var className = $"{rawName}Event";
        var finalPath = Path.Combine(Path.GetDirectoryName(fileName), $"{className}.cs");
        var content = InterfaceTemplate.Replace("#NAME#", rawName);

        try
        {
            File.WriteAllText(AssetDatabase.GenerateUniqueAssetPath(finalPath), content);
        }
        catch (Exception ex)
        {
            EditorUtility.DisplayDialog(Title, ex.Message, "Close");
            return ex.Message;
        }

        AssetDatabase.Refresh();
        return null;
    }

    static string SanitizeClassName(string className)
    {
        var sb = new StringBuilder();
        foreach (var c in className)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
                sb.Append(c);
        }
        return sb.ToString();
    }

    static Texture2D GetIcon()
    {
        return EditorGUIUtility.IconContent("cs Script Icon").image as Texture2D;
    }

    static void CreateAndRenameAsset(string fileName, Texture2D icon, Action<string> onSuccess)
    {
        var action = CreateInstance<CustomEndNameAction>();
        action.Callback = onSuccess;
        ProjectWindowUtil.StartNameEditingIfProjectWindowExists(0, action, fileName, icon, null);
    }

    sealed class CustomEndNameAction : EndNameEditAction
    {
        [NonSerialized] public Action<string> Callback;

        public override void Action(int instanceId, string pathName, string resourceFile)
        {
            Callback?.Invoke(pathName);
        }
    }
}
