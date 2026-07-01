using System.IO;
using Game.Core.Configs;
using UnityEditor;
using UnityEngine;

namespace Game.Core.EditorTools
{
    /// <summary>
    /// Доступ к единственному ассету <see cref="KeyRegistry"/> из редактора:
    /// находит его в проекте (где бы ни лежал) или создаёт в Resources при первом обращении.
    /// </summary>
    public static class KeyRegistryEditorUtil
    {
        const string DefaultPath = "Assets/Resources/KeyRegistry.asset";

        static KeyRegistry _cached;

        /// <summary>Найти реестр (без создания). Может вернуть null, если его ещё нет.</summary>
        public static KeyRegistry Find()
        {
            if (_cached != null) return _cached;

            foreach (var guid in AssetDatabase.FindAssets("t:KeyRegistry"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                _cached = AssetDatabase.LoadAssetAtPath<KeyRegistry>(path);
                if (_cached != null) return _cached;
            }
            return null;
        }

        /// <summary>Найти или создать реестр в Assets/Resources.</summary>
        public static KeyRegistry GetOrCreate()
        {
            var registry = Find();
            if (registry != null) return registry;

            Directory.CreateDirectory("Assets/Resources");
            registry = ScriptableObject.CreateInstance<KeyRegistry>();
            AssetDatabase.CreateAsset(registry, DefaultPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            _cached = registry;
            Debug.Log($"[KeyRegistry] Создан новый реестр ключей: {DefaultPath}");
            return registry;
        }
    }
}
