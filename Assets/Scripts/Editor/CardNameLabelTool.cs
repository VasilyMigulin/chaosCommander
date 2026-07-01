using System;
using System.Collections.Generic;
using Game.Core.Instance.Card;
using UnityEditor;
using UnityEngine;

namespace Game.Core.EditorTools
{
    /// <summary>
    /// Дублирует ИМЯ КАРТЫ (CardInstanceData.CardData.Name) в LABELS ассета, чтобы в пикере (ObjectField)
    /// можно было искать карту не только по имени файла, но и по её игровому имени: набираешь l:&lt;слово&gt;.
    ///
    /// Labels хранятся в .meta (НЕ в .asset-графе [SerializeReference]) → данные карты не трогаются,
    /// corruption невозможен. SetLabels ПЕРЕЗАПИСЫВАЕТ метки ассета (если вешаешь свои метки руками —
    /// учти; обычно на картах их нет).
    ///
    /// Запуск вручную: Tools/Cards/Sync Name Labels (после правки имён). Авто-синк на импорт — опционально
    /// (см. AssetPostprocessor ниже, по умолчанию включён только для папки расширений).
    /// </summary>
    public static class CardNameLabelTool
    {
        const string ExpansionRoot = "Assets/Resources/Expansion";

        [MenuItem("Tools/Cards/Sync Name Labels")]
        static void SyncNameLabels()
        {
            if (!AssetDatabase.IsValidFolder(ExpansionRoot))
            {
                Debug.LogWarning($"[CardNameLabel] Папка не найдена: {ExpansionRoot}");
                return;
            }

            int updated = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:CardInstanceData", new[] { ExpansionRoot }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var card = AssetDatabase.LoadAssetAtPath<CardInstanceData>(path);
                if (Apply(card)) updated++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[CardNameLabel] Готово. Обновлено меток: {updated}. В пикере ищи как l:<слово_из_имени>.");
        }

        /// <summary>Вешает на ассет метки = слова имени карты (+ цельное имя через '_'). Идемпотентно:
        /// пишет ТОЛЬКО если метки реально изменились (можно звать хоть каждую перерисовку инспектора).
        /// false — нечего вешать (нет модели/имени) или уже актуально. Labels живут в .meta → безопасно
        /// даже во время правки [SerializeReference]-ассета (не трогаем граф, без Undo/SetDirty).</summary>
        public static bool Apply(CardInstanceData card)
        {
            if (card == null || card.CardData == null || string.IsNullOrWhiteSpace(card.CardData.Name))
                return false;

            var want = BuildLabels(card.CardData.Name);
            if (SameLabels(AssetDatabase.GetLabels(card), want)) return false;

            AssetDatabase.SetLabels(card, want);
            return true;
        }

        static bool SameLabels(string[] a, string[] b)
        {
            if (a.Length != b.Length) return false;
            var set = new HashSet<string>(a, StringComparer.OrdinalIgnoreCase);
            foreach (var x in b) if (!set.Contains(x)) return false;
            return true;
        }

        static string[] BuildLabels(string name)
        {
            // Отдельные слова имени (поиск l:<слово>) + цельное имя без пробелов (l:Вонючее_облако).
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var w in name.Split(new[] { ' ', '\t', '-', ',', '.' }, StringSplitOptions.RemoveEmptyEntries))
                set.Add(w);
            set.Add(name.Replace(' ', '_'));

            var arr = new string[set.Count];
            set.CopyTo(arr);
            return arr;
        }

        /// <summary>Авто-синк меток при импорте/перемещении карты в папке расширений (имя могло поменяться).</summary>
        sealed class LabelPostprocessor : AssetPostprocessor
        {
            static void OnPostprocessAllAssets(
                string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                bool changed = false;
                foreach (var p in imported) changed |= TrySync(p);
                foreach (var p in moved)    changed |= TrySync(p);
                if (changed) AssetDatabase.SaveAssets();
            }

            static bool TrySync(string path)
            {
                if (string.IsNullOrEmpty(path) || !path.StartsWith(ExpansionRoot) || !path.EndsWith(".asset"))
                    return false;
                return Apply(AssetDatabase.LoadAssetAtPath<CardInstanceData>(path));
            }
        }
    }
}
