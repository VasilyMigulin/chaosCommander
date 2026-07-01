using System.Collections.Generic;
using Game.Core.Configs;
using Game.Core.Instance.Card;
using UnityEditor;
using UnityEngine;

namespace Game.Core.EditorTools
{
    /// <summary>
    /// При создании/перемещении карты (CardInstanceData) в папке расширения
    /// Assets/Resources/Expansion/&lt;Expansion&gt;/... (включая подпапки):
    ///   1) выдаёт ей уникальный Id (CardData.Id) в пределах расширения — не нужно
    ///      проставлять 0,1,2 вручную, дубликат сделать невозможно;
    ///   2) автоматически добавляет её в ExpansionConfig этого расширения
    ///      (файл *.asset типа ExpansionConfig в корне папки расширения).
    ///
    /// Id монотонный (max+1, без переиспользования удалённых). Первая карта = Id 0.
    /// Обработка отложена через delayCall — ассеты не меняются прямо в колбэке импорта.
    /// </summary>
    public sealed class CardIdAssigner : AssetPostprocessor
    {
        const string ExpansionRoot = "Assets/Resources/Expansion/";

        static readonly HashSet<string> _pending = new HashSet<string>();
        static bool _scheduled;

        static void OnPostprocessAllAssets(
            string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromAssetPaths)
        {
            Enqueue(importedAssets);
            Enqueue(movedAssets);

            if (_pending.Count == 0 || _scheduled) return;

            _scheduled = true;
            EditorApplication.delayCall += ProcessPending;
        }

        static void Enqueue(string[] paths)
        {
            foreach (var p in paths)
                if (IsExpansionCardPath(p))
                    _pending.Add(p);
        }

        static void ProcessPending()
        {
            _scheduled = false;

            var paths = new List<string>(_pending);
            _pending.Clear();
            paths.Sort(System.StringComparer.Ordinal); // детерминированный порядок при пакетном импорте

            bool changed = false;
            foreach (var path in paths)
                changed |= ProcessCard(path);

            if (changed)
                AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Публичная точка входа: гарантированно обрабатывает карту (уникальный Id + регистрация в
        /// ExpansionConfig) НЕ дожидаясь импорта. Нужна, когда модель в CardData назначают уже ПОСЛЕ
        /// создания ассета — тогда постпроцессор её пропустил (CardData был null). Зовётся из инспектора.
        /// </summary>
        public static void EnsureProcessed(CardInstanceData card)
        {
            if (card == null) return;
            string path = AssetDatabase.GetAssetPath(card);
            if (string.IsNullOrEmpty(path) || !IsExpansionCardPath(path)) return;

            if (ProcessCard(path))
                AssetDatabase.SaveAssets();
        }

        static bool ProcessCard(string cardPath)
        {
            var card = AssetDatabase.LoadAssetAtPath<CardInstanceData>(cardPath);
            if (card == null) return false;

            string expFolder = GetExpansionFolder(cardPath);
            if (expFolder == null) return false;

            bool changed = false;

            // Id назначаем только когда модель уже задана (Id живёт на CardData)
            if (card.CardData != null)
                changed |= AssignUniqueId(card, cardPath, expFolder);

            // В список расширения добавляем в любом случае (даже без модели)
            changed |= RegisterInExpansion(card, expFolder);

            return changed;
        }

        // ── Уникальный Id ───────────────────────────────────────────────────────

        static bool AssignUniqueId(CardInstanceData card, string cardPath, string expFolder)
        {
            var used = CollectUsedIds(expFolder, excludePath: cardPath);
            if (!used.Contains(card.CardData.Id)) return false; // уже уникален

            int newId = NextFreeId(used);
            // НЕ используем Undo.RecordObject: на ассете с [SerializeReference] (CardData + способности)
            // он в ряде версий Unity обнуляет managed-ссылки. Просто меняем Id и помечаем dirty.
            card.CardData.Id = newId;
            EditorUtility.SetDirty(card);
            Debug.Log($"[CardIdAssigner] {cardPath} → Id {newId} (расширение {expFolder})", card);
            return true;
        }

        static HashSet<int> CollectUsedIds(string expansionFolder, string excludePath)
        {
            var used = new HashSet<int>();
            foreach (var guid in AssetDatabase.FindAssets("t:CardInstanceData", new[] { expansionFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path == excludePath) continue;

                var other = AssetDatabase.LoadAssetAtPath<CardInstanceData>(path);
                if (other != null && other.CardData != null)
                    used.Add(other.CardData.Id);
            }
            return used;
        }

        static int NextFreeId(HashSet<int> used)
        {
            int max = -1;
            foreach (var id in used)
                if (id > max) max = id;
            return max + 1; // монотонно, без переиспользования удалённых Id
        }

        // ── Регистрация в ExpansionConfig ───────────────────────────────────────

        static bool RegisterInExpansion(CardInstanceData card, string expFolder)
        {
            var config = FindExpansionConfig(expFolder);
            if (config == null) return false;

            if (config.Cards == null)
                config.Cards = new List<CardInstanceData>();

            if (config.Cards.Contains(card)) return false;

            Undo.RecordObject(config, "Register Card In Expansion");
            config.Cards.Add(card);
            EditorUtility.SetDirty(config);
            Debug.Log($"[CardIdAssigner] '{card.name}' добавлена в ExpansionConfig '{config.name}'", config);
            return true;
        }

        static ExpansionConfig FindExpansionConfig(string expansionFolder)
        {
            var guids = AssetDatabase.FindAssets("t:ExpansionConfig", new[] { expansionFolder });
            if (guids.Length == 0)
            {
                Debug.LogWarning($"[CardIdAssigner] В '{expansionFolder}' не найден ExpansionConfig — карта не добавлена в список.");
                return null;
            }
            if (guids.Length > 1)
                Debug.LogWarning($"[CardIdAssigner] В '{expansionFolder}' несколько ExpansionConfig — беру первый.");

            return AssetDatabase.LoadAssetAtPath<ExpansionConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        // ── Пути ────────────────────────────────────────────────────────────────

        static bool IsExpansionCardPath(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith(ExpansionRoot) || !path.EndsWith(".asset"))
                return false;

            var type = AssetDatabase.GetMainAssetTypeAtPath(path);
            return type != null && typeof(CardInstanceData).IsAssignableFrom(type);
        }

        /// <summary>Папка расширения: Assets/Resources/Expansion/&lt;Exp&gt; (первый сегмент после Expansion/).</summary>
        static string GetExpansionFolder(string path)
        {
            int start = ExpansionRoot.Length;
            int slash = path.IndexOf('/', start);
            return slash < 0 ? null : path.Substring(0, slash);
        }

        // ── Ручные утилиты ──────────────────────────────────────────────────────

        [MenuItem("Tools/Cards/Fix Duplicate Card IDs")]
        static void FixAllDuplicates()
        {
            string root = ExpansionRoot.TrimEnd('/');
            if (!AssetDatabase.IsValidFolder(root))
            {
                Debug.LogWarning($"[CardIdAssigner] Папка не найдена: {root}");
                return;
            }

            int fixedCount = 0;
            foreach (var expFolder in AssetDatabase.GetSubFolders(root))
            {
                var paths = new List<string>();
                foreach (var guid in AssetDatabase.FindAssets("t:CardInstanceData", new[] { expFolder }))
                    paths.Add(AssetDatabase.GUIDToAssetPath(guid));
                paths.Sort(System.StringComparer.Ordinal);

                var used = new HashSet<int>();
                foreach (var path in paths)
                {
                    var card = AssetDatabase.LoadAssetAtPath<CardInstanceData>(path);
                    if (card == null || card.CardData == null) continue;

                    if (used.Contains(card.CardData.Id))
                    {
                        int newId = NextFreeId(used);
                        card.CardData.Id = newId;   // без Undo (см. AssignUniqueId): защита SerializeReference
                        EditorUtility.SetDirty(card);
                        Debug.Log($"[CardIdAssigner] {path} → Id {newId} (расширение {expFolder})", card);
                        fixedCount++;
                    }

                    used.Add(card.CardData.Id);
                }
            }

            if (fixedCount > 0) AssetDatabase.SaveAssets();
            Debug.Log($"[CardIdAssigner] Готово. Исправлено дубликатов: {fixedCount}.");
        }

        /// <summary>
        /// Чистит «осиротевшие» managed-ссылки на УДАЛЁННЫЕ/переименованные [SerializeReference]-типы
        /// (напр. снесённые PlayerTargetFilter/CreatureTargetFilter). Такие ссылки ломают десериализацию
        /// всего графа карты → CardData грузится как null («different serialization layout / missing type»).
        /// Используем официальный UnityEditor.SerializationUtility, без ручной правки YAML.
        /// </summary>
        [MenuItem("Tools/Cards/Clear Missing Ability Refs")]
        static void ClearMissingAbilityRefs()
        {
            string root = ExpansionRoot.TrimEnd('/');
            if (!AssetDatabase.IsValidFolder(root))
            {
                Debug.LogWarning($"[CardIdAssigner] Папка не найдена: {root}");
                return;
            }

            var paths = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:CardInstanceData", new[] { root }))
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));

            // КЛЮЧЕВОЕ: принудительный реимпорт. Unity грузит ассеты из library-кэша, собранного
            // КОГДА удалённые типы ещё существовали → метаданные «missing types» не записаны, и
            // HasManagedReferencesWithMissingTypes возвращает false. Реимпорт переоценивает YAML
            // против ТЕКУЩИХ типов и регистрирует отсутствующие.
            foreach (var path in paths)
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            int cleaned = 0;
            foreach (var path in paths)
            {
                var card = AssetDatabase.LoadAssetAtPath<CardInstanceData>(path);
                if (card == null) continue;

                if (SerializationUtility.HasManagedReferencesWithMissingTypes(card))
                {
                    SerializationUtility.ClearAllManagedReferencesWithMissingTypes(card);
                    EditorUtility.SetDirty(card);
                    cleaned++;
                    Debug.Log($"[CardIdAssigner] Очищены битые ссылки на удалённые типы: {path}", card);
                }
            }

            if (cleaned > 0) AssetDatabase.SaveAssets();
            Debug.Log($"[CardIdAssigner] Готово. Карт с очищенными битыми ссылками: {cleaned} (реимпортировано: {paths.Count}).");
        }

        [MenuItem("Tools/Cards/Sync Expansion Card Lists")]
        static void SyncAllExpansionLists()
        {
            string root = ExpansionRoot.TrimEnd('/');
            if (!AssetDatabase.IsValidFolder(root))
            {
                Debug.LogWarning($"[CardIdAssigner] Папка не найдена: {root}");
                return;
            }

            int added = 0;
            foreach (var expFolder in AssetDatabase.GetSubFolders(root))
            {
                var config = FindExpansionConfig(expFolder);
                if (config == null) continue;
                if (config.Cards == null) config.Cards = new List<CardInstanceData>();

                foreach (var guid in AssetDatabase.FindAssets("t:CardInstanceData", new[] { expFolder }))
                {
                    var card = AssetDatabase.LoadAssetAtPath<CardInstanceData>(AssetDatabase.GUIDToAssetPath(guid));
                    if (card == null || config.Cards.Contains(card)) continue;

                    Undo.RecordObject(config, "Sync Expansion Cards");
                    config.Cards.Add(card);
                    EditorUtility.SetDirty(config);
                    added++;
                }
            }

            if (added > 0) AssetDatabase.SaveAssets();
            Debug.Log($"[CardIdAssigner] Готово. Добавлено в списки расширений: {added}.");
        }
    }
}
