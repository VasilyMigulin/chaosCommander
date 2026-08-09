using System.Collections.Generic;
using Game.Core.Instance.Card;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace Game.Core.EditorTools
{
    /// <summary>
    /// Инспектор CardPool: поля критериев (рисует Odin — нужно для [SerializeReference] ICreatureTag Archetype)
    /// + кнопка Rebuild, которая сканирует все CardInstanceData под Assets/Resources/Expansion и заполняет
    /// Cards теми, что проходят CardPool.Matches. Скан (AssetDatabase) — editor-only здесь; матчинг — в CardPool.
    /// Наследуем OdinEditor (как CardInstanceDataEditor), иначе дефолтный инспектор не рисует SerializeReference-пикер.
    /// </summary>
    [CustomEditor(typeof(CardPool))]
    public sealed class CardPoolEditor : OdinEditor
    {
        const string Root = "Assets/Resources/Expansion";

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();   // Odin рисует все поля, включая [SerializeReference] Archetype

            var pool = (CardPool)target;

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("🔄 Rebuild — собрать карты по критериям", GUILayout.Height(28)))
                    { Rebuild(pool); AssetDatabase.SaveAssets(); }
            }
            EditorGUILayout.LabelField($"В пуле карт: {(pool.Cards != null ? pool.Cards.Count : 0)}", EditorStyles.miniBoldLabel);
        }

        /// <summary>Пересобрать ВСЕ CardPool в проекте (Tools-меню или авто после регистрации карт). Один скан
        /// всех CardInstanceData на пул — жать после добавления/переименования карт.</summary>
        [MenuItem("Tools/Card Pools/Rebuild All Pools")]
        public static void RebuildAllPools()
        {
            var guids = AssetDatabase.FindAssets("t:CardPool");
            int n = 0;
            foreach (var guid in guids)
            {
                var pool = AssetDatabase.LoadAssetAtPath<CardPool>(AssetDatabase.GUIDToAssetPath(guid));
                if (pool == null) continue;
                Rebuild(pool);
                n++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[CardPool] Пересобрано пулов: {n}.");
        }

        // Собрать один пул (SaveAssets НЕ вызываем — батч RebuildAllPools/кнопка сохраняют разом).
        static void Rebuild(CardPool pool)
        {
            var list = new List<CardInstanceData>();
            foreach (var guid in AssetDatabase.FindAssets("t:CardInstanceData", new[] { Root }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var card = AssetDatabase.LoadAssetAtPath<CardInstanceData>(path);
                if (card == null || card.CardData == null) continue;
                if (!pool.Matches(card.CardData, path)) continue;
                list.Add(card);
            }
            list.Sort((a, b) => string.Compare(a.CardData.Name, b.CardData.Name, System.StringComparison.OrdinalIgnoreCase));

            Undo.RecordObject(pool, "Rebuild CardPool");
            pool.Cards = list;
            EditorUtility.SetDirty(pool);
            Debug.Log($"[CardPool] '{pool.name}': собрано {list.Count} карт по критериям.");
        }
    }
}
