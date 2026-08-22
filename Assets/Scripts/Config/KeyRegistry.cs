using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Core.Configs
{
    /// <summary>
    /// Единое хранилище строковых ключей, разбитых по разделам (Expansion и любые
    /// другие, которые ты добавляешь сам). Поля с атрибутом [KeyDropdown("Раздел")]
    /// рисуют выпадающий список ключей этого раздела вместо ручного ввода.
    ///
    /// Редактируется через окно Tools → Chaos Commander → Реестр ключей.
    /// Лежит в Resources, чтобы был единый доступ из любого места.
    /// </summary>
    [CreateAssetMenu(fileName = "KeyRegistry", menuName = "Data/Key Registry")]
    public sealed class KeyRegistry : ScriptableObject
    {
        // Имена «системных» разделов, на которые ссылаемся из кода.
        public const string SectionExpansion = "Expansion";
        public const string SectionArchetype = "Archetype";

        [Serializable]
        public sealed class Section
        {
            public string Name;
            public List<string> Keys = new List<string>();
        }

        public List<Section> Sections = new List<Section>();

        /// <summary>Ключи раздела (пустой массив, если раздела нет).</summary>
        public IReadOnlyList<string> GetKeys(string section)
        {
            var s = FindSection(section);
            return s != null ? (IReadOnlyList<string>)s.Keys : Array.Empty<string>();
        }

        public IReadOnlyList<string> SectionNames()
        {
            var names = new List<string>(Sections.Count);
            foreach (var s in Sections) names.Add(s.Name);
            return names;
        }

        public Section FindSection(string section)
        {
            if (string.IsNullOrEmpty(section)) return null;
            foreach (var s in Sections)
                if (s.Name == section) return s;
            return null;
        }

        public Section GetOrCreateSection(string section)
        {
            var s = FindSection(section);
            if (s == null)
            {
                s = new Section { Name = section };
                Sections.Add(s);
            }
            return s;
        }

        /// <summary>Добавить ключ в раздел. Возвращает true, если действительно добавлен (нет дубля).</summary>
        public bool AddKey(string section, string key)
        {
            key = key?.Trim();
            if (string.IsNullOrEmpty(key)) return false;

            var s = GetOrCreateSection(section);
            if (s.Keys.Contains(key)) return false;

            s.Keys.Add(key);
            s.Keys.Sort(StringComparer.OrdinalIgnoreCase);
            return true;
        }

        public bool RemoveKey(string section, string key)
        {
            var s = FindSection(section);
            return s != null && s.Keys.Remove(key);
        }
    }
}
