using System.Collections.Generic;
using System.Text;
using Game.Core.Configs;
using UnityEditor;
using UnityEngine;

namespace Game.Core.EditorTools
{
    /// <summary>
    /// Экспорт TaskConfigAsset → Title Data JSON "taskConfig". Формат совпадает с тем, что читают серверные
    /// функции (resets / loginRewards / daily / weekly; reward: currencies/cards/boosters/avatars — только
    /// НЕ пустые массивы). Меню: Tools → Backend → Export Task Config. Результат заливаешь в Content → Title
    /// Data ключом "taskConfig". id задач пустой → генерится из типа (d_/w_ + type), дубли id — предупреждение.
    /// </summary>
    public static class TaskConfigExportTool
    {
        [MenuItem("Tools/Backend/Export Task Config (Title Data 'taskConfig')")]
        public static void Export()
        {
            var asset = TitleDataExportUtil.FindSingleAsset<TaskConfigAsset>("[TaskExport]");
            if (asset == null)
            {
                Debug.LogWarning("[TaskExport] Нет ассета TaskConfigAsset. Создай: Create → Game → Task Config.");
                return;
            }

            string outPath = EditorUtility.SaveFilePanel("Export Task Config",
                Application.dataPath + "/..", "taskConfig", "json");
            if (string.IsNullOrEmpty(outPath)) return;

            System.IO.File.WriteAllText(outPath, BuildJson(asset), Encoding.UTF8);
            Debug.Log($"[TaskExport] taskConfig (daily {asset.Daily.Count}, weekly {asset.Weekly.Count}, login {asset.LoginRewards.Count}) → {outPath}.");
            EditorUtility.RevealInFinder(outPath);
        }

        static string BuildJson(TaskConfigAsset a)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");

            sb.Append($"  \"resets\": {{ \"dailyHourUtc\": {a.Resets.DailyHourUtc}, \"weeklyDayUtc\": {a.Resets.WeeklyDayUtc}, \"weeklyHourUtc\": {a.Resets.WeeklyHourUtc} }},\n\n");

            sb.Append("  \"loginRewards\": [\n");
            for (int i = 0; i < a.LoginRewards.Count; i++)
            {
                var lr = a.LoginRewards[i];
                sb.Append($"    {{ \"day\": {lr.Day}, \"reward\": {BuildReward(lr.Reward)} }}");
                sb.Append(i < a.LoginRewards.Count - 1 ? ",\n" : "\n");
            }
            sb.Append("  ],\n\n");

            AppendTasks(sb, "daily", a.Daily, weekly: false);
            sb.Append(",\n\n");
            AppendTasks(sb, "weekly", a.Weekly, weekly: true);
            sb.Append("\n}");
            return sb.ToString();
        }

        static void AppendTasks(StringBuilder sb, string key, List<TaskConfigAsset.TaskEntry> tasks, bool weekly)
        {
            var seen = new HashSet<string>();
            sb.Append($"  \"{key}\": [\n");
            for (int i = 0; i < tasks.Count; i++)
            {
                var t = tasks[i];
                if (t == null) continue;
                string id = t.ResolveId(weekly);
                if (!seen.Add(id))
                    Debug.LogWarning($"[TaskExport] Дубль id '{id}' в '{key}' — задайте уникальный Id (сервер клеймит по id).");
                sb.Append($"    {{ \"id\": \"{TitleDataExportUtil.Esc(id)}\", \"type\": \"{TaskConfigAsset.TypeString(t.Type)}\", \"target\": {Mathf.Max(1, t.Target)}, \"reward\": {BuildReward(t.Reward)} }}");
                sb.Append(i < tasks.Count - 1 ? ",\n" : "\n");
            }
            sb.Append("  ]");
        }

        // reward: только НЕ пустые массивы (как в существующем taskConfig.json). Пустая награда → "{ }".
        static string BuildReward(TaskConfigAsset.RewardConfig r)
        {
            var parts = new List<string>();
            if (r != null)
            {
                if (r.Currencies != null && r.Currencies.Count > 0)
                {
                    var items = new List<string>();
                    foreach (var c in r.Currencies)
                        if (c != null && !string.IsNullOrEmpty(c.Code) && c.Amount != 0)
                            items.Add($"{{ \"code\": \"{TitleDataExportUtil.Esc(c.Code)}\", \"amount\": {c.Amount} }}");
                    if (items.Count > 0) parts.Add($"\"currencies\": [ {string.Join(", ", items)} ]");
                }
                if (r.Cards != null && r.Cards.Count > 0)
                {
                    var items = new List<string>();
                    foreach (var c in r.Cards)
                    {
                        string id = c?.ResolveItemId();
                        if (!string.IsNullOrEmpty(id)) items.Add($"{{ \"itemId\": \"{TitleDataExportUtil.Esc(id)}\", \"amount\": {Mathf.Max(1, c.Amount)} }}");
                    }
                    if (items.Count > 0) parts.Add($"\"cards\": [ {string.Join(", ", items)} ]");
                }
                if (r.Boosters != null && r.Boosters.Count > 0)
                {
                    var items = new List<string>();
                    foreach (var b in r.Boosters) if (!string.IsNullOrEmpty(b)) items.Add($"\"{TitleDataExportUtil.Esc(b.ToLowerInvariant())}\"");
                    if (items.Count > 0) parts.Add($"\"boosters\": [ {string.Join(", ", items)} ]");
                }
                if (r.Avatars != null && r.Avatars.Count > 0)
                {
                    var items = new List<string>();
                    foreach (var av in r.Avatars) if (!string.IsNullOrEmpty(av)) items.Add($"\"{TitleDataExportUtil.Esc(av.ToLowerInvariant())}\"");
                    if (items.Count > 0) parts.Add($"\"avatars\": [ {string.Join(", ", items)} ]");
                }
            }
            return parts.Count > 0 ? "{ " + string.Join(", ", parts) + " }" : "{ }";
        }
    }
}
