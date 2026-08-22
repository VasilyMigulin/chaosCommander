using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Game.Core.Configs;
using UnityEditor;
using UnityEngine;

namespace Game.Core.EditorTools
{
    /// <summary>
    /// Экспорт BoosterConfigAsset → Title Data JSON "boosterConfig" (серверные дроп-таблицы для OpenBooster).
    /// Пишем ТОЛЬКО серверную часть: itemId → { expansion (из ExpansionConfig.ExpansionId), cardCount, slots[{weights}] }.
    /// Визуал (иконка/префаб-вьюшка) НЕ экспортируется — он живёт в ассете на клиенте. Меню: Tools → Backend →
    /// Export Booster Config. Результат заливаешь в Content → Title Data ключом "boosterConfig".
    /// </summary>
    public static class BoosterConfigExportTool
    {
        [MenuItem("Tools/Backend/Export Booster Config (Title Data 'boosterConfig')")]
        public static void Export()
        {
            var asset = TitleDataExportUtil.FindSingleAsset<BoosterConfigAsset>("[BoosterExport]");
            if (asset == null)
            {
                Debug.LogWarning("[BoosterExport] Нет ассета BoosterConfigAsset. Создай: Create → Game → Booster Config.");
                return;
            }

            var boosters = new List<BoosterConfigAsset.Booster>();
            var seen = new HashSet<string>();
            foreach (var b in asset.Boosters)
            {
                if (b == null) continue;
                string id = string.IsNullOrEmpty(b.ItemId) ? null : b.ItemId.ToLowerInvariant();
                if (string.IsNullOrEmpty(id)) { Debug.LogWarning("[BoosterExport] Бустер без ItemId — пропущен."); continue; }
                if (!seen.Add(id)) Debug.LogWarning($"[BoosterExport] Дубль ItemId '{id}' — в JSON останется последний.");
                if (b.Expansion == null || string.IsNullOrEmpty(b.Expansion.ExpansionId))
                    Debug.LogWarning($"[BoosterExport] '{id}': не задан Expansion (сет) — сервер не найдёт пул карт.");
                if (b.Slots == null || b.Slots.Length == 0)
                    Debug.LogWarning($"[BoosterExport] '{id}': нет слотов — бустер выдаст 0 карт.");
                boosters.Add(b);
            }

            string outPath = EditorUtility.SaveFilePanel("Export Booster Config",
                Application.dataPath + "/..", "boosterConfig", "json");
            if (string.IsNullOrEmpty(outPath)) return;

            System.IO.File.WriteAllText(outPath, BuildJson(boosters), Encoding.UTF8);
            Debug.Log($"[BoosterExport] boosterConfig ({boosters.Count} бустеров) → {outPath}");
            EditorUtility.RevealInFinder(outPath);
        }

        static string BuildJson(List<BoosterConfigAsset.Booster> boosters)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            for (int i = 0; i < boosters.Count; i++)
            {
                var b = boosters[i];
                string id = b.ItemId.ToLowerInvariant();
                string exp = b.Expansion != null ? (b.Expansion.ExpansionId ?? "").ToLowerInvariant() : "";
                int count = b.Slots != null ? b.Slots.Length : 0;

                sb.Append($"  \"{TitleDataExportUtil.Esc(id)}\": {{\n");
                sb.Append($"    \"expansion\": \"{TitleDataExportUtil.Esc(exp)}\",\n");
                sb.Append($"    \"cardCount\": {count},\n");
                if (!string.IsNullOrEmpty(b.RequiresCampaign))
                    sb.Append($"    \"requiresCampaign\": \"{TitleDataExportUtil.Esc(b.RequiresCampaign.ToLowerInvariant())}\",\n");
                sb.Append("    \"slots\": [\n");

                if (b.Slots != null)
                    for (int s = 0; s < b.Slots.Length; s++)
                    {
                        sb.Append("      { \"weights\": { ").Append(WeightsJson(b.Slots[s])).Append(" } }");
                        sb.Append(s < b.Slots.Length - 1 ? ",\n" : "\n");
                    }

                sb.Append("    ]\n");
                sb.Append("  }");
                sb.Append(i < boosters.Count - 1 ? ",\n" : "\n");
            }
            sb.Append("}");
            return sb.ToString();
        }

        // { "common": 0.8, "rare": 0.18, ... } — только веса > 0.
        static string WeightsJson(BoosterConfigAsset.Slot slot)
        {
            if (slot == null || slot.Weights == null) return "";
            var parts = new List<string>();
            foreach (var w in slot.Weights)
            {
                if (w == null || w.Weight <= 0f) continue;
                string rar = w.Rarity.ToString().ToLowerInvariant();
                parts.Add($"\"{rar}\": {w.Weight.ToString("0.####", CultureInfo.InvariantCulture)}");
            }
            return string.Join(", ", parts);
        }
    }
}
