#if UNITY_LOCALIZATION_PRESENT
using System.Collections.Generic;
using System.IO;
using System.Text;
using Game.Core.Instance.Card;
using Game.Core.Shared;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization.Tables;

namespace Game.Core.Localization.Editor
{
    /// <summary>
    /// Редактор-тулы для наполнения String Table Collection "CardText" из ассетов карт.
    /// Ключи: card.{expansionId}.{id}.name / .desc (см. CardTextLocalization). Русский язык
    /// заполняется авторскими Name/Description (это ШАБЛОН — со звёздочками *N* и обычными
    /// фразами; форматтер обрабатывает их в рантайме). Английский оставляем пустым — заполняется
    /// переводом (через CSV-импорт или вручную в окне Localization Tables); пустой EN → в игре
    /// сработает фоллбэк на русский, ничего не ломается.
    ///
    /// ПЕРЕД запуском: создайте Locale ru/en и String Table Collection с именем "CardText"
    /// (Window → Asset Management → Localization Tables). См. LOCALIZATION_SETUP.md.
    /// </summary>
    public static class CardLocTableBuilder
    {
        const string TableName = "CardText";

        [MenuItem("Tools/Localization/Build CardText table (RU from assets)")]
        public static void BuildFromAssets()
        {
            var collection = LocalizationEditorSettings.GetStringTableCollection(TableName);
            if (collection == null)
            {
                Debug.LogError($"[Loc] String Table Collection '{TableName}' не найдена. " +
                    "Создайте её: Window → Asset Management → Localization Tables → New Table Collection " +
                    "(String, имя 'CardText'), затем повторите. См. LOCALIZATION_SETUP.md.");
                return;
            }

            var shared = collection.SharedData;
            int cards = 0, keys = 0;

            foreach (var card in LoadAllCards())
            {
                var model = card.CardData;
                if (model == null) continue;
                string exp = string.IsNullOrEmpty(model.ExpansionId) ? GuessExpansion(card) : model.ExpansionId;

                keys += UpsertRu(collection, shared, CardTextLocalization.NameKey(exp, model.Id), model.Name);
                keys += UpsertRu(collection, shared, CardTextLocalization.DescKey(exp, model.Id), model.Description);
                cards++;
            }

            EditorUtility.SetDirty(shared);
            foreach (var t in collection.StringTables) EditorUtility.SetDirty(t);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Loc] CardText: обработано карт {cards}, ключей затронуто {keys}. EN оставлен для перевода.");
        }

        [MenuItem("Tools/Localization/Export CardText keys to CSV (for translation)")]
        public static void ExportCsv()
        {
            var collection = LocalizationEditorSettings.GetStringTableCollection(TableName);
            if (collection == null) { Debug.LogError($"[Loc] Нет коллекции '{TableName}'."); return; }

            var ru = FindTable(collection, "ru");
            var sb = new StringBuilder("key;ru;en\n");
            foreach (var entry in collection.SharedData.Entries)
            {
                string key = entry.Key;
                string ruVal = ru != null ? (ru.GetEntry(entry.Id)?.Value ?? "") : "";
                sb.Append(Escape(key)).Append(';').Append(Escape(ruVal)).Append(";\n");
            }

            string path = Path.Combine(Application.dataPath, "Localization", "card_text_ru.csv");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
            AssetDatabase.Refresh();
            Debug.Log($"[Loc] Экспортировано в {path}. Заполните колонку en и импортируйте обратно.");
        }

        [MenuItem("Tools/Localization/Import EN from CSV")]
        public static void ImportEnCsv()
        {
            var collection = LocalizationEditorSettings.GetStringTableCollection(TableName);
            if (collection == null) { Debug.LogError($"[Loc] Нет коллекции '{TableName}'."); return; }
            string path = Path.Combine(Application.dataPath, "Localization", "card_text_ru.csv");
            if (!File.Exists(path)) { Debug.LogError($"[Loc] Нет файла {path}."); return; }

            var en = FindTable(collection, "en");
            if (en == null) { Debug.LogError("[Loc] Нет таблицы en в коллекции."); return; }
            var shared = collection.SharedData;

            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            int applied = 0;
            for (int i = 1; i < lines.Length; i++)   // пропускаем заголовок
            {
                var cols = SplitCsv(lines[i]);
                if (cols.Count < 3) continue;
                string key = cols[0], enVal = cols[2];
                if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(enVal)) continue;
                long id = shared.GetId(key);
                if (id == SharedTableData.EmptyId) continue;
                var entry = en.GetEntry(id) ?? en.AddEntry(id, "");
                entry.Value = enVal;
                applied++;
            }
            EditorUtility.SetDirty(en);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Loc] Импортировано EN-строк: {applied}.");
        }

        [MenuItem("Tools/Localization/Import full CardText CSV (key;ru;en, creates keys)")]
        public static void ImportFullCsv()
        {
            var collection = LocalizationEditorSettings.GetStringTableCollection(TableName);
            if (collection == null) { Debug.LogError($"[Loc] Нет коллекции '{TableName}'. Создайте её (см. LOCALIZATION_SETUP.md)."); return; }

            string path = Path.Combine(Application.dataPath, "Localization", "card_text.csv");
            if (!File.Exists(path)) { Debug.LogError($"[Loc] Нет файла {path}."); return; }

            var shared = collection.SharedData;
            var ru = FindTable(collection, "ru");
            var en = FindTable(collection, "en");
            if (ru == null || en == null) { Debug.LogError("[Loc] В коллекции нет таблиц ru и/или en."); return; }

            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            int rows = 0;
            for (int i = 1; i < lines.Length; i++)   // пропускаем заголовок
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                var cols = SplitCsv(lines[i]);
                if (cols.Count < 2) continue;
                string key = cols[0];
                if (string.IsNullOrEmpty(key)) continue;
                string ruVal = cols.Count > 1 ? cols[1] : "";
                string enVal = cols.Count > 2 ? cols[2] : "";

                long id = shared.GetId(key);
                if (id == SharedTableData.EmptyId) id = shared.AddKey(key).Id;

                if (ruVal != null) { var e = ru.GetEntry(id) ?? ru.AddEntry(id, ""); e.Value = ruVal; }
                if (!string.IsNullOrEmpty(enVal)) { var e = en.GetEntry(id) ?? en.AddEntry(id, ""); e.Value = enVal; }
                rows++;
            }

            EditorUtility.SetDirty(shared);
            EditorUtility.SetDirty(ru);
            EditorUtility.SetDirty(en);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Loc] Импортировано строк (ru+en): {rows} из {path}.");
        }

        // ── helpers ──

        // Возвращает 1 если ключ был добавлен/обновлён в RU.
        static int UpsertRu(StringTableCollection collection, SharedTableData shared, string key, string ru)
        {
            long id = shared.GetId(key);
            if (id == SharedTableData.EmptyId) id = shared.AddKey(key).Id;

            var ruTable = FindTable(collection, "ru");
            if (ruTable == null) return 0;
            var entry = ruTable.GetEntry(id) ?? ruTable.AddEntry(id, "");
            entry.Value = ru ?? string.Empty;

            // Гарантируем, что EN-запись существует (пустая) — чтобы её было видно для перевода.
            var enTable = FindTable(collection, "en");
            if (enTable != null && enTable.GetEntry(id) == null) enTable.AddEntry(id, "");
            return 1;
        }

        static StringTable FindTable(StringTableCollection collection, string langPrefix)
        {
            foreach (var t in collection.StringTables)
                if (t.LocaleIdentifier.Code != null && t.LocaleIdentifier.Code.StartsWith(langPrefix))
                    return t;
            return null;
        }

        static IEnumerable<CardInstanceData> LoadAllCards()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:CardInstanceData"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var card = AssetDatabase.LoadAssetAtPath<CardInstanceData>(path);
                if (card != null) yield return card;
            }
        }

        // expansionId не сохранён? — выводим из папки (.../Expansion/Standard/... → "standard").
        static string GuessExpansion(CardInstanceData card)
        {
            string path = AssetDatabase.GetAssetPath(card);
            int i = path.IndexOf("/Expansion/", System.StringComparison.OrdinalIgnoreCase);
            if (i < 0) return "standard";
            var rest = path.Substring(i + "/Expansion/".Length);
            int slash = rest.IndexOf('/');
            return (slash > 0 ? rest.Substring(0, slash) : rest).ToLowerInvariant();
        }

        static string Escape(string s) => s == null ? "" : "\"" + s.Replace("\"", "\"\"") + "\"";

        static List<string> SplitCsv(string line)
        {
            var res = new List<string>();
            var sb = new StringBuilder();
            bool inQ = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQ)
                {
                    if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else if (c == '"') inQ = false;
                    else sb.Append(c);
                }
                else
                {
                    if (c == '"') inQ = true;
                    else if (c == ';') { res.Add(sb.ToString()); sb.Clear(); }
                    else sb.Append(c);
                }
            }
            res.Add(sb.ToString());
            return res;
        }
    }
}
#endif
