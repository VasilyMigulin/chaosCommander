using System.Collections.Generic;
using System.Text;
using Game.Core.Configs;
using UnityEditor;
using UnityEngine;

namespace Game.Core.EditorTools
{
    /// <summary>
    /// Экспорт BlackMarketPoolAsset → Title Data JSON "blackMarketConfig". Карты пула группируются по редкости,
    /// itemId берётся из CardInstanceData (нижний регистр, как в каталоге). Меню: Tools → Backend → Export
    /// Black Market Config. Результат заливаешь в Content → Title Data ключом "blackMarketConfig".
    /// </summary>
    public static class BlackMarketConfigExportTool
    {
        static readonly string[] Order = { "common", "rare", "epic", "legendary", "exotic" };

        [MenuItem("Tools/Backend/Export Black Market Config (Title Data 'blackMarketConfig')")]
        public static void Export()
        {
            var asset = TitleDataExportUtil.FindSingleAsset<BlackMarketPoolAsset>("[BlackMarketExport]");
            if (asset == null)
            {
                Debug.LogWarning("[BlackMarketExport] Нет ассета BlackMarketPoolAsset. Создай: Create → Game → Black Market Pool.");
                return;
            }

            // slots + prices по редкостям (из настроек ассета).
            var slots = new Dictionary<string, int>();
            var priceCode = new Dictionary<string, string>();
            var priceAmount = new Dictionary<string, int>();
            if (asset.Rarities != null)
                foreach (var r in asset.Rarities)
                {
                    string key = r.Rarity.ToString().ToLowerInvariant();
                    slots[key] = r.Slots;
                    priceCode[key] = string.IsNullOrEmpty(r.PriceCode) ? "GD" : r.PriceCode;
                    priceAmount[key] = r.PriceAmount;
                }

            // pools: группируем карты по ИХ редкости, itemId — из карты (нижний регистр).
            var pools = new Dictionary<string, List<string>>();
            int skipped = 0;
            foreach (var card in asset.Pool)
            {
                if (card == null || card.CardData == null) { skipped++; continue; }
                string id = card.ItemId;
                if (string.IsNullOrEmpty(id)) { skipped++; continue; }
                string key = card.CardData.Rarity.ToString().ToLowerInvariant();
                if (!pools.TryGetValue(key, out var list)) pools[key] = list = new List<string>();
                if (!list.Contains(id)) list.Add(id);   // без дублей
            }

            string outPath = EditorUtility.SaveFilePanel("Export Black Market Config",
                Application.dataPath + "/..", "blackMarketConfig", "json");
            if (string.IsNullOrEmpty(outPath)) return;

            System.IO.File.WriteAllText(outPath, BuildJson(asset, slots, priceCode, priceAmount, pools), Encoding.UTF8);
            Debug.Log($"[BlackMarketExport] blackMarketConfig → {outPath} (пропущено пустых: {skipped}).");

            // Предупреждаем, если в пуле не хватает карт под слоты (сервер выдаст меньше офферов).
            foreach (var key in Order)
            {
                int need = slots.ContainsKey(key) ? slots[key] : 0;
                int have = pools.ContainsKey(key) ? pools[key].Count : 0;
                if (need > 0 && have < need)
                    Debug.LogWarning($"[BlackMarketExport] '{key}': в пуле {have} карт, а слотов {need} — набор будет неполным.");
            }
            EditorUtility.RevealInFinder(outPath);
        }

        static string BuildJson(BlackMarketPoolAsset a, Dictionary<string, int> slots,
            Dictionary<string, string> priceCode, Dictionary<string, int> priceAmount, Dictionary<string, List<string>> pools)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append($"  \"rotation\": {{ \"weeklyDayUtc\": {a.WeeklyDayUtc}, \"hourUtc\": {a.HourUtc} }},\n");

            // slots — только заданные редкости.
            var slotParts = new List<string>();
            foreach (var r in Order) if (slots.ContainsKey(r)) slotParts.Add($"\"{r}\": {slots[r]}");
            sb.Append("  \"slots\": { ").Append(string.Join(", ", slotParts)).Append(" },\n");

            // prices.
            var priceParts = new List<string>();
            foreach (var r in Order) if (priceCode.ContainsKey(r))
                priceParts.Add($"    \"{r}\": {{ \"code\": \"{TitleDataExportUtil.Esc(priceCode[r])}\", \"amount\": {priceAmount[r]} }}");
            sb.Append("  \"prices\": {\n").Append(string.Join(",\n", priceParts)).Append("\n  },\n");

            // pools.
            var poolParts = new List<string>();
            foreach (var r in Order) if (pools.ContainsKey(r))
            {
                var quoted = pools[r].ConvertAll(id => $"\"{TitleDataExportUtil.Esc(id)}\"");
                poolParts.Add($"    \"{r}\": [{string.Join(", ", quoted)}]");
            }
            sb.Append("  \"pools\": {\n").Append(string.Join(",\n", poolParts)).Append("\n  }\n");

            sb.Append("}");
            return sb.ToString();
        }
    }
}
