using System.Collections.Generic;
using System.Text;
using Game.Core.Configs;
using UnityEditor;
using UnityEngine;

namespace Game.Core.EditorTools
{
    /// <summary>
    /// Экспорт ShopConfigAsset → Title Data JSON "shopConfig". itemId берётся из инстанса (нижний регистр,
    /// как в каталоге) либо из ItemIdOverride (бустеры). Меню: Tools → Backend → Export Shop Config.
    /// Результат заливаешь в Content → Title Data ключом "shopConfig".
    /// </summary>
    public static class ShopConfigExportTool
    {
        [MenuItem("Tools/Backend/Export Shop Config (Title Data 'shopConfig')")]
        public static void Export()
        {
            var asset = FindAsset();
            if (asset == null)
            {
                Debug.LogWarning("[ShopExport] Нет ассета ShopConfigAsset. Создай: Create → Game → Shop Config.");
                return;
            }

            var entries = new List<ShopConfigAsset.Entry>();
            var seen = new HashSet<string>();
            int skipped = 0;
            foreach (var e in asset.Entries)
            {
                if (e == null) { skipped++; continue; }
                string id = e.ResolveItemId();
                if (string.IsNullOrEmpty(id)) { skipped++; continue; }
                // Дубль включённого itemId ломает findShopEntry/«Куплено» (сервер берёт первый).
                if (e.Enabled && !seen.Add(id))
                    Debug.LogWarning($"[ShopExport] Дубль включённого itemId '{id}' — сервер возьмёт первый, «Куплено» сломается. Заведи отдельный itemId в каталоге.");
                entries.Add(e);
            }

            string outPath = EditorUtility.SaveFilePanel("Export Shop Config",
                Application.dataPath + "/..", "shopConfig", "json");
            if (string.IsNullOrEmpty(outPath)) return;

            System.IO.File.WriteAllText(outPath, BuildJson(entries), Encoding.UTF8);
            Debug.Log($"[ShopExport] shopConfig ({entries.Count} офферов) → {outPath} (пропущено пустых: {skipped}).");
            EditorUtility.RevealInFinder(outPath);
        }

        static ShopConfigAsset FindAsset()
        {
            var guids = AssetDatabase.FindAssets("t:ShopConfigAsset");
            if (guids.Length == 0) return null;
            if (guids.Length > 1)
                Debug.LogWarning($"[ShopExport] Ассетов ShopConfigAsset: {guids.Length} — беру первый.");
            return AssetDatabase.LoadAssetAtPath<ShopConfigAsset>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        static string BuildJson(List<ShopConfigAsset.Entry> entries)
        {
            var sb = new StringBuilder();
            sb.Append("{\n  \"entries\": [\n");
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                string id = e.ResolveItemId();
                string code = string.IsNullOrEmpty(e.PriceCode) ? "GD" : e.PriceCode;
                int qty = e.Quantity > 0 ? e.Quantity : 1;
                string name = string.IsNullOrEmpty(e.DisplayName) ? id : e.DisplayName;

                sb.Append("    { ");
                sb.Append($"\"itemId\": \"{Esc(id)}\", ");
                sb.Append($"\"displayName\": \"{Esc(name)}\", ");
                sb.Append($"\"category\": \"{e.ResolveCategory()}\", ");
                sb.Append($"\"priceCode\": \"{Esc(code)}\", ");
                sb.Append($"\"priceAmount\": {e.PriceAmount}, ");
                sb.Append($"\"quantity\": {qty}, ");
                sb.Append($"\"unique\": {(e.Unique ? "true" : "false")}, ");
                sb.Append($"\"enabled\": {(e.Enabled ? "true" : "false")}");
                sb.Append(" }");
                sb.Append(i < entries.Count - 1 ? ",\n" : "\n");
            }
            sb.Append("  ]\n}");
            return sb.ToString();
        }

        // Минимальный JSON-эскейп для строк (кавычки/бэкслеш/переводы строк).
        static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
        }
    }
}
