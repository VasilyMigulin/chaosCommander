using System;

namespace Game.Core.Configs
{
    /// <summary>
    /// Помечает строковое поле как «ключ из раздела реестра» (см. <see cref="KeyRegistry"/>).
    /// В инспекторе вместо ручного ввода рисуется выпадающий список ключей именно
    /// этого раздела. Пример: [KeyDropdown(KeyRegistry.SectionExpansion)] на ExpansionId.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public sealed class KeyDropdownAttribute : Attribute
    {
        public readonly string Section;

        public KeyDropdownAttribute(string section)
        {
            Section = section;
        }
    }
}
