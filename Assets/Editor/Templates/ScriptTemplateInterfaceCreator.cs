using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text;
using System;
using UnityEditor.ProjectWindowCallback;

public sealed class ScriptTemplateInterfaceCreator : ScriptableObject
{
    const string Title = "Interface template generator";
    const string TargetPath = "Assets/Scripts/Shared/Interface";
    const string Namespace = "Game.Core.Shared.Interface";

    const string InterfaceTemplate =
        "namespace " + Namespace + "\n" +
        "{\n" +
        "    public interface #NAME#\n" +
        "    {\n" +
        "\n" +
        "    }\n" +
        "}\n";

    [MenuItem("Assets/Create/Scripting/Interface (Game.Core.Interface)", false, 80)]
    static void CreateInterface()
    {
        if (!AssetDatabase.IsValidFolder(TargetPath))
        {
            Directory.CreateDirectory(TargetPath);
            AssetDatabase.Refresh();
        }

        CreateAndRenameAsset($"{TargetPath}/INewInterface.cs", GetIcon(),
            (name) => CreateTemplateInternal(name));
    }

    static string CreateTemplateInternal(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            EditorUtility.DisplayDialog(Title, "Invalid filename", "Close");
            return "Invalid filename";
        }

        var className = SanitizeClassName(Path.GetFileNameWithoutExtension(fileName));
        var content = InterfaceTemplate.Replace("#NAME#", className);

        try
        {
            File.WriteAllText(AssetDatabase.GenerateUniqueAssetPath(fileName), content);
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
