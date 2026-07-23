using System;
using UnityEngine;

namespace Game.Core.Shared
{
    /// <summary>
    /// Типобезопасный строковый идентификатор (вместо «голых» string-ключей). Сериализуется, годится
    /// для инспектора и как ключ словаря/множества (Equals/GetHashCode по значению). Общий для всего
    /// проекта — известные id объявляй в реестрах-константах, а не строками по месту. Для удобства
    /// есть неявное приведение из string, но предпочитай именованные константы.
    /// </summary>
    [Serializable]
    public struct RegistryId : IEquatable<RegistryId>
    {
        [SerializeField] private string _value;

        public RegistryId(string value) { _value = value; }

        public string Value => _value;
        public bool IsEmpty => string.IsNullOrEmpty(_value);

        public bool Equals(RegistryId other) => string.Equals(_value, other._value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is RegistryId r && Equals(r);
        public override int GetHashCode() => _value != null ? _value.GetHashCode() : 0;
        public override string ToString() => _value ?? string.Empty;

        public static bool operator ==(RegistryId a, RegistryId b) => a.Equals(b);
        public static bool operator !=(RegistryId a, RegistryId b) => !a.Equals(b);
        public static implicit operator RegistryId(string value) => new RegistryId(value);
    }
}
