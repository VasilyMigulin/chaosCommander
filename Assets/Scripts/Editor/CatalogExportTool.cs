using System;
using System.Collections.Generic;
using System.Text;
using Game.Core.Instance.Card;
using Game.Core.Service;
using UnityEditor;
using UnityEngine;

namespace Game.Core.EditorTools
{
    /// <summary>
    /// Экспорт каталога карт для PlayFab Economy v2.
    /// Сканирует все CardInstanceData под Assets/Resources/Expansion и выгружает JSON, где каждая
    /// карта = catalog item с item id "{expansionId}_{cardId}" (контракт CardItemId на клиенте +
    /// catalog-seeding на сервере). ExpansionId берём из пути (папка под Expansion/), т.к. в ассете
    /// поле заполняется только в рантайме (ExpansionConfig.Rebuild).
    ///
    /// Полученный JSON используется как источник для заведения предметов в каталоге (через Economy
    /// bulk-import в Game Manager либо серверный seeder). Меню: Tools → Backend → Export Card Catalog.
    /// </summary>
    public static class CatalogExportTool
    {
        const string Root = "Assets/Resources/Expansion";

        [Serializable]
        class CatalogEntry
        {
            public string id;          // "{expansionId}_{cardId}"
            public string title;       // отображаемое имя
            public string type;        // "card"
            public string expansion;   // тег
            public string rarity;      // тег: common/rare/epic/legendary/exotic
            public string element;     // теги цветов через '|'
            public string cardType;    // creature/spell/charm
            public int    cost;        // базовая стоимость (справочно)
        }

        [Serializable]
        class CatalogFile
        {
            public string generatedAtUtc;
            public int    count;
            public List<CatalogEntry> items = new List<CatalogEntry>();
        }

        [MenuItem("Tools/Backend/Export Card Catalog (JSON)")]
        public static void Export()
        {
            var file = new CatalogFile { generatedAtUtc = DateTime.UtcNow.ToString("o") };

            foreach (var guid in AssetDatabase.FindAssets("t:CardInstanceData", new[] { Root }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var card = AssetDatabase.LoadAssetAtPath<CardInstanceData>(path);
                if (card == null || card.CardData == null) continue;

                string expansionId = ExpansionFromPath(path);
                if (string.IsNullOrEmpty(expansionId))
                {
                    Debug.LogWarning($"[CatalogExport] Не удалось вывести ExpansionId из пути: {path} — пропуск.");
                    continue;
                }

                var m = card.CardData;
                file.items.Add(new CatalogEntry
                {
                    id        = $"{expansionId}_{m.Id}",
                    title     = m.Name,
                    type      = "card",
                    expansion = expansionId,
                    rarity    = m.Rarity.ToString().ToLowerInvariant(),
                    element   = ElementTags(m.Element),
                    cardType  = m.GetCardType().ToString().ToLowerInvariant(),
                    cost      = m.PlayCostAmount,
                });
            }

            file.items.Sort((a, b) => string.Compare(a.id, b.id, StringComparison.OrdinalIgnoreCase));
            file.count = file.items.Count;

            string outPath = EditorUtility.SaveFilePanel(
                "Export Card Catalog", Application.dataPath + "/..", "card_catalog", "json");
            if (string.IsNullOrEmpty(outPath)) return;

            System.IO.File.WriteAllText(outPath, JsonUtility.ToJson(file, true), Encoding.UTF8);
            Debug.Log($"[CatalogExport] Выгружено {file.count} карт → {outPath}");
            EditorUtility.RevealInFinder(outPath);
        }

        [MenuItem("Tools/Backend/Export Card Pool (Title Data 'cardPool')")]
        public static void ExportPool()
        {
            // expansion → rarity → [itemId]. Читается Azure Function OpenBooster.
            var pool = new Dictionary<string, Dictionary<string, List<string>>>();

            foreach (var guid in AssetDatabase.FindAssets("t:CardInstanceData", new[] { Root }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var card = AssetDatabase.LoadAssetAtPath<CardInstanceData>(path);
                if (card == null || card.CardData == null) continue;

                string expansionId = ResolveExpansionId(card, path);
                if (string.IsNullOrEmpty(expansionId)) continue;
                if (card.CardData.IsToken) continue;   // токены не в бустеры

                string rarity = card.CardData.Rarity.ToString().ToLowerInvariant();
                if (!pool.TryGetValue(expansionId, out var byRarity))
                    pool[expansionId] = byRarity = new Dictionary<string, List<string>>();
                if (!byRarity.TryGetValue(rarity, out var ids))
                    byRarity[rarity] = ids = new List<string>();
                ids.Add($"{expansionId}_{card.CardData.Id}");
            }

            string outPath = EditorUtility.SaveFilePanel(
                "Export Card Pool", Application.dataPath + "/..", "cardPool", "json");
            if (string.IsNullOrEmpty(outPath)) return;

            // JsonUtility не сериализует Dictionary — собираем JSON вручную (валидно для Title Data).
            System.IO.File.WriteAllText(outPath, PoolToJson(pool), Encoding.UTF8);
            Debug.Log($"[CatalogExport] cardPool → {outPath}");
            EditorUtility.RevealInFinder(outPath);
        }

        static string PoolToJson(Dictionary<string, Dictionary<string, List<string>>> pool)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            int ei = 0;
            foreach (var exp in pool)
            {
                sb.Append($"  \"{exp.Key}\": {{\n");
                int ri = 0;
                foreach (var rarity in exp.Value)
                {
                    sb.Append($"    \"{rarity.Key}\": [");
                    for (int i = 0; i < rarity.Value.Count; i++)
                    {
                        sb.Append($"\"{rarity.Value[i]}\"");
                        if (i < rarity.Value.Count - 1) sb.Append(", ");
                    }
                    sb.Append(']');
                    sb.Append(++ri < exp.Value.Count ? ",\n" : "\n");
                }
                sb.Append("  }");
                sb.Append(++ei < pool.Count ? ",\n" : "\n");
            }
            sb.Append('}');
            return sb.ToString();
        }

        // ── Каталог в формате PlayFab Catalog v1 (готов к загрузке) ─────────────────

        /// <summary>
        /// Экспорт каталога СРАЗУ в формате, который принимает Game Manager
        /// (Economy → Catalogs → Upload Catalog) — то же, что отдаёт Download Catalog.
        /// Прошлый экспорт («Export Card Catalog») даёт наш собственный JSON: он удобен для сверки,
        /// но PlayFab его не съест, а карт сотни — руками не завести.
        ///
        /// Важное про IsStackable = false: клиент считает количество копий карты как ЧИСЛО
        /// ItemInstance с этим ItemId (EconomyService/PlayerLibrary). Со стековым предметом PlayFab
        /// схлопнул бы копии в один инстанс с counter'ом, и библиотека показала бы 1 карту вместо 4.
        ///
        /// Бустеры и аватары дописываются сюда же — их id должны совпадать с Title Data shopConfig
        /// и boosterConfig, иначе GrantItemsToUser упадёт и покупка откатится.
        /// </summary>
        [MenuItem("Tools/Backend/Export PlayFab Catalog v1 (upload-ready)")]
        public static void ExportPlayFabCatalog()
        {
            const string catalogVersion = "main";

            var items = new List<string>();

            foreach (var guid in AssetDatabase.FindAssets("t:CardInstanceData", new[] { Root }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var card = AssetDatabase.LoadAssetAtPath<CardInstanceData>(path);
                if (card == null || card.CardData == null) continue;

                string expansionId = ResolveExpansionId(card, path);
                if (string.IsNullOrEmpty(expansionId)) continue;

                var m = card.CardData;
                // Токены (Паук, Вонючий туман, стори-карты) игрок не владеет — спавнятся в бою как ECS-сущности,
                // не в инвентаре, не в бустерах, не в покупках. Каталог = «чем можно владеть», токенам тут не место
                // (та же логика, что в Export Card Pool). Иначе они раздувают счёт коллекции.
                if (m.IsToken) continue;

                var tags = new List<string>
                {
                    "card",
                    "rarity_" + m.Rarity.ToString().ToLowerInvariant(),
                    "expansion_" + expansionId,
                };

                items.Add(CatalogItemJson($"{expansionId}_{m.Id}", m.Name, "card", catalogVersion, tags));
            }

            // Бустеры и аватары: id ОБЯЗАНЫ совпадать с Server/TitleData/shopConfig.json и boosterConfig.json.
            items.Add(CatalogItemJson("booster_standard", "Бустер «Standard»", "booster", catalogVersion,
                                      new List<string> { "booster" }));
            items.Add(CatalogItemJson("booster_premium", "Бустер «Премиум»", "booster", catalogVersion,
                                      new List<string> { "booster" }));
            items.Add(CatalogItemJson("booster_stolen_princess", "Бустер «Похищение принцессы»", "booster", catalogVersion,
                                      new List<string> { "booster" }));
            items.Add(CatalogItemJson("avatar_prince", "Аватар «Шальной принц»", "avatar", catalogVersion,
                                      new List<string> { "avatar" }));
            items.Add(CatalogItemJson("avatar_gnidalf", "Аватар «Гнидальф»", "avatar", catalogVersion,
                                      new List<string> { "avatar" }));

            string outPath = EditorUtility.SaveFilePanel(
                "Export PlayFab Catalog v1", Application.dataPath + "/..", "playfab_catalog_v1", "json");
            if (string.IsNullOrEmpty(outPath)) return;

            var sb = new StringBuilder();
            sb.Append("{\n  \"CatalogVersion\": \"").Append(catalogVersion).Append("\",\n  \"Catalog\": [\n");
            for (int i = 0; i < items.Count; i++)
            {
                sb.Append(items[i]);
                sb.Append(i < items.Count - 1 ? ",\n" : "\n");
            }
            // DropTables на верхнем уровне — как в файле, который отдаёт Download JSON (см. формат PlayFab).
            // Пустой массив: дроп-таблицы бустеров живут в Title Data cardPool, а не в каталоге.
            sb.Append("  ],\n  \"DropTables\": []\n}");

            System.IO.File.WriteAllText(outPath, sb.ToString(), Encoding.UTF8);
            Debug.Log($"[CatalogExport] PlayFab Catalog v1: {items.Count} предметов → {outPath}");
            EditorUtility.RevealInFinder(outPath);
        }

        static string CatalogItemJson(string itemId, string displayName, string itemClass,
                                      string catalogVersion, List<string> tags)
        {
            var sb = new StringBuilder();
            sb.Append("    {\n");
            sb.Append("      \"ItemId\": \"").Append(Escape(itemId)).Append("\",\n");
            sb.Append("      \"DisplayName\": \"").Append(Escape(displayName)).Append("\",\n");
            sb.Append("      \"ItemClass\": \"").Append(Escape(itemClass)).Append("\",\n");
            sb.Append("      \"CatalogVersion\": \"").Append(catalogVersion).Append("\",\n");
            sb.Append("      \"IsStackable\": false,\n");     // копия = отдельный ItemInstance (см. коммент выше)
            sb.Append("      \"IsTradable\": false,\n");
            sb.Append("      \"Tags\": [");
            for (int i = 0; i < tags.Count; i++)
            {
                sb.Append('"').Append(Escape(tags[i])).Append('"');
                if (i < tags.Count - 1) sb.Append(", ");
            }
            sb.Append("]\n");
            sb.Append("    }");
            return sb.ToString();
        }

        static string Escape(string s)
            => string.IsNullOrEmpty(s) ? "" : s.Replace("\\", "\\\\").Replace("\"", "\\\"");

        /// <summary>
        /// Настоящий ExpansionId для ItemId. КРИТИЧНО брать его из данных карты (напр. "standard"),
        /// а НЕ из имени папки ("Standard"): PlayFab ItemId регистрозависим, а клиент резолвит инвентарь
        /// по CardData.ExpansionId (его пишет ExpansionConfig.Rebuild из ассета экспеншена). Если каталог
        /// собрать по имени папки, регистр разойдётся → выданные карты не смэтчатся, коллекция пустая.
        /// Фолбэк на имя папки (в нижнем регистре) — только если в ассете id ещё не проставлен.
        /// </summary>
        static string ResolveExpansionId(CardInstanceData card, string assetPath)
        {
            var fromData = card != null && card.CardData != null ? card.CardData.ExpansionId : null;
            if (!string.IsNullOrEmpty(fromData)) return fromData;
            return ExpansionFromPath(assetPath)?.ToLowerInvariant();
        }

        /// <summary>ExpansionId = первая папка под Assets/Resources/Expansion/.</summary>
        static string ExpansionFromPath(string assetPath)
        {
            const string marker = "/Expansion/";
            int i = assetPath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;
            string rest = assetPath.Substring(i + marker.Length);
            int slash = rest.IndexOf('/');
            return slash > 0 ? rest.Substring(0, slash) : null;
        }

        static string ElementTags(EnumService.Element element)
        {
            var parts = new List<string>();
            foreach (EnumService.Element flag in Enum.GetValues(typeof(EnumService.Element)))
                if ((element & flag) != 0) parts.Add(flag.ToString().ToLowerInvariant());
            return string.Join("|", parts);
        }
    }
}
