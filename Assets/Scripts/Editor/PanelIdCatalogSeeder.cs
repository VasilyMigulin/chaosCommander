using AwesomeUI.Core.Panel;
using AwesomeUI.Core.Window;
using Game.Core.Shared;
using UnityEditor;
using UnityEngine;

namespace Game.Core.EditorTools
{
    /// <summary>
    /// Засев каталога RegistryId именами всех панелей (SourcePanel-наследников). Имя класса = дефолтный
    /// SourcePanel.PanelId, поэтому после засева дропдаун у AwesomeButton._targetPanelId сразу содержит
    /// все панели проекта — не нужно вбивать id руками. Абстрактные базы (напр. ComingSoonPanel) пропускаем.
    /// Меню: Tools → UI → Sync Panel Ids to Catalog.
    /// </summary>
    public static class PanelIdCatalogSeeder
    {
        [MenuItem("Tools/UI/Sync Panel Ids to Catalog")]
        public static void Sync()
        {
            var catalog = RegistryIdCatalogUtil.GetOrCreate();
            if (catalog == null)
            {
                Debug.LogWarning("[PanelIdCatalog] Каталог не найден и не создан.");
                return;
            }

            int added = 0;
            foreach (var type in TypeCache.GetTypesDerivedFrom<SourcePanel>())
            {
                if (type.IsAbstract) continue;
                if (catalog.Add(RegistryCategories.Panel, type.Name)) added++;
            }
            // Окна тоже — чтобы их id были в дропдауне (напр. для _hideForPanels у MenuHudPanel).
            foreach (var type in TypeCache.GetTypesDerivedFrom<SourceWindow>())
            {
                if (type.IsAbstract) continue;
                if (catalog.Add(RegistryCategories.Panel, type.Name)) added++;
            }

            var ids = catalog.GetIds(RegistryCategories.Panel);
            ids.Sort();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();

            Debug.Log($"[PanelIdCatalog] Категория '{RegistryCategories.Panel}': +{added} новых id " +
                      $"(всего {ids.Count}). Ассет: {AssetDatabase.GetAssetPath(catalog)}");
        }
    }
}
