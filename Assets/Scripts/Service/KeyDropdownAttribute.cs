using System;

namespace Game.Core.Service
{
    /// <summary>
    /// Помечает строковое поле как «ключ из раздела реестра» (см. Game.Core.Configs.KeyRegistry).
    /// В инспекторе вместо ручного ввода рисуется выпадающий список ключей именно этого раздела.
    /// Пример: [KeyDropdown(KeyRegistry.SectionExpansion)] на ExpansionId. Атрибут лежит здесь
    /// (Service), а не рядом с KeyRegistry (Configs) — Configs ссылается на Model.Card → Ability,
    /// поэтому обратная ссылка Ability → Configs дала бы цикл сборок; Service — общий низкоуровневый
    /// узел, на который уже ссылаются и Ability, и Configs, так что новых ссылок сборок не требуется.
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
