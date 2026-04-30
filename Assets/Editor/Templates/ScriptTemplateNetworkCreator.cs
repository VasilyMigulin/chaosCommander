using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text;
using System;
using UnityEditor.ProjectWindowCallback;

public sealed class ScriptTemplateNetworkCreator : ScriptableObject
{
    const string Title = "Network template generator";
    const string TargetPath = "Assets/Scripts/Photon";
    const string Namespace = "Game.Core.Photon";

    const string InterfaceTemplate =
        "using Fusion;\n" +
        "namespace " + Namespace + "\n" +
        "{\n" +
        "    public struct Network#NAME#Data : INetworkStruct\n" +
        "    {\n" +
        "\n" +
        "    }\n" +
        "}\n";

    [MenuItem("Assets/Create/Scripting/Network (Game.Core.Photon)", false, 80)]
    static void CreateInterface()
    {
        if (!AssetDatabase.IsValidFolder(TargetPath))
        {
            Directory.CreateDirectory(TargetPath);
            AssetDatabase.Refresh();
        }

        CreateAndRenameAsset($"{TargetPath}/NetworkData.cs", GetIcon(),
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

        // Strip existing "Network" prefix and "Data" suffix to avoid duplication
        if (rawName.StartsWith("Network", StringComparison.Ordinal))
            rawName = rawName.Substring("Network".Length);
        if (rawName.EndsWith("Data", StringComparison.Ordinal))
            rawName = rawName.Substring(0, rawName.Length - "Data".Length);

        if (string.IsNullOrEmpty(rawName))
        {
            EditorUtility.DisplayDialog(Title, "Invalid filename", "Close");
            return "Invalid filename";
        }

        var className = $"Network{rawName}Data";
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
