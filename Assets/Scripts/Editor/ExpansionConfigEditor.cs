using System.Collections.Generic;
using System.IO;
using Game.Core.Configs;
using Game.Core.Instance.Card;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace Game.Core.EditorTools
{
    /// <summary>
    /// Кастомный инспектор ExpansionConfig: добавляет кнопку «Собрать карты из папки
    /// расширения». Сканирует папку, в которой лежит сам ассет конфига (и все её
    /// подпапки), находит все CardInstanceData и переписывает список Cards — чтобы
    /// не вносить карты вручную при каждом добавлении.
    ///
    /// Наследуется от OdinEditor — тело конфига рисуется Odin как обычно, кастом
    /// только добавляет блок кнопок сверху.
    /// </summary>
    [CustomEditor(typeof(ExpansionConfig))]
    public sealed class ExpansionConfigEditor : OdinEditor
    {
        public override void OnInspectorGUI()
        {
            var config = (ExpansionConfig)target;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                string folder = GetAssetFolder(config);
                EditorGUILayout.LabelField("Папка расширения:", string.IsNullOrEmpty(folder) ? "<не сохранён>" : folder, EditorStyles.miniLabel);

                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(folder)))
                {
                    if (GUILayout.Button("📦 Собрать карты из папки расширения"))
                        CollectCards(config, folder);
                }
            }

            EditorGUILayout.Space();
            base.OnInspectorGUI();
        }

        static string GetAssetFolder(Object asset)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            return string.IsNullOrEmpty(path) ? null : Path.GetDirectoryName(path).Replace('\\', '/');
        }

        static void CollectCards(ExpansionConfig config, string folder)
        {
            var found = new List<CardInstanceData>();
            foreach (var guid in AssetDatabase.FindAssets("t:CardInstanceData", new[] { folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var card = AssetDatabase.LoadAssetAtPath<CardInstanceData>(path);
                if (card != null) found.Add(card);
            }

            // Стабильный порядок: по CardId, чтобы список не «прыгал» между сборками.
            found.Sort((a, b) => a.CardId.CompareTo(b.CardId));

            Undo.RecordObject(config, "Collect Expansion Cards");
            config.Cards.Clear();
            config.Cards.AddRange(found);
            config.Rebuild();

            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssetIfDirty(config);

            Debug.Log($"[ExpansionConfig:{config.ExpansionId}] Собрано {found.Count} карт из «{folder}».");
        }
    }
}
