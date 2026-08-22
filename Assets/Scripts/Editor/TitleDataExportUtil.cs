using UnityEditor;
using UnityEngine;

namespace Game.Core.EditorTools
{
    /// <summary>
    /// Общие хелперы Title Data export-тулз (Tools → Backend → Export * — Shop/Booster/Task/BlackMarket).
    /// Раньше каждый из 4 экспортёров копировал руками и поиск единственного ассета, и JSON-эскейп строк —
    /// и они разошлись: BoosterConfigExportTool.Esc() не экранировал "\n"/"\r", а три остальных экранировали
    /// (баг: бустер с переносом строки в RequiresCampaign/описании сломал бы JSON молча). Теперь один
    /// источник истины для обоих — новый экспортёр просто зовёт эти методы, а не копирует их снова.
    /// </summary>
    internal static class TitleDataExportUtil
    {
        /// <summary>Единственный ассет типа T в проекте — так каждый Export ищет свой конфиг. tag —
        /// префикс лога вызывающего тулза (напр. "[BoosterExport]") для предупреждения о дублях.</summary>
        public static T FindSingleAsset<T>(string tag) where T : Object
        {
            var guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
            if (guids.Length == 0) return null;
            if (guids.Length > 1)
                Debug.LogWarning($"{tag} Ассетов {typeof(T).Name}: {guids.Length} — беру первый.");
            return AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        /// <summary>Минимальный JSON-эскейп для строк (кавычки/бэкслеш/переводы строк).</summary>
        public static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
        }
    }
}
