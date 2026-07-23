using System;

namespace Game.Core.Shared
{
    /// <summary>
    /// Категория для поля RegistryId — задаёт, из какого раздела каталога брать выпадающий список
    /// (и куда добавлять новые id). Вешается на поле: [RegistryCategory(RegistryCategories.Panel)].
    /// Без атрибута поле использует общую категорию (General).
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public sealed class RegistryCategoryAttribute : Attribute
    {
        public string Category { get; }
        public RegistryCategoryAttribute(string category) { Category = category; }
    }

    /// <summary>Известные категории id (расширяй по мере надобности).</summary>
    public static class RegistryCategories
    {
        public const string General = "General";
        public const string Panel   = "UI/Panel";
        public const string Notify  = "UI/Notify";
        public const string Service = "Service";
    }
}
