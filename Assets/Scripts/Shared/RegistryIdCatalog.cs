using System.Collections.Generic;
using UnityEngine;

namespace Game.Core.Shared
{
    /// <summary>
    /// Каталог зарегистрированных RegistryId, РАЗБИТЫЙ ПО КАТЕГОРИЯМ (UI/Panel, UI/Notify, Service, …).
    /// Наполняется через инспектор-дропдаун у поля RegistryId (кнопка «+»), категория берётся из
    /// [RegistryCategory] на поле. Один ассет на проект.
    /// </summary>
    [CreateAssetMenu(fileName = "RegistryIdCatalog", menuName = "Data/RegistryId Catalog")]
    public class RegistryIdCatalog : ScriptableObject
    {
        [System.Serializable]
        public class Category
        {
            public string Name;
            public List<string> Ids = new List<string>();
        }

        public List<Category> Categories = new List<Category>();

        public List<string> GetIds(string category)
        {
            var c = Find(category);
            return c != null ? c.Ids : new List<string>();
        }

        /// <summary>Добавить id в категорию (создаёт категорию при необходимости). true — если добавлено.</summary>
        public bool Add(string category, string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            var c = Find(category);
            if (c == null) { c = new Category { Name = category }; Categories.Add(c); }
            if (c.Ids.Contains(id)) return false;
            c.Ids.Add(id);
            return true;
        }

        Category Find(string name)
        {
            foreach (var c in Categories)
                if (c.Name == name) return c;
            return null;
        }
    }
}
