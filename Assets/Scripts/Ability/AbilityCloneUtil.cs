using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace Game.Core.Ability
{
    // === class (OOP) ===
    /// <summary>
    /// Рекурсивная глубокая копия authored-графа способности (триггеры/правила/эффекты/условия
    /// и их вложенные контейнеры). Нужна, чтобы две сущности одной карты не делили стейт.
    /// Клонирует только данные-шаблона: value-типы и строки копируются как есть,
    /// Unity-ассеты (Sprite и т.п.) НЕ клонируются (общая ссылка), делегаты на шаблоне = null.
    /// </summary>
    public static class AbilityCloneUtil
    {
        public static T DeepClone<T>(T source) => (T)CloneObject(source);

        static object CloneObject(object obj)
        {
            if (obj == null) return null;

            var type = obj.GetType();
            if (type.IsValueType || obj is string) return obj;          // копируются по значению
            if (obj is UnityEngine.Object) return obj;                  // ассеты не клонируем
            if (obj is Delegate) return null;                           // подписки на шаблоне игнорируем

            if (obj is Array array)
            {
                var clone = Array.CreateInstance(type.GetElementType()!, array.Length);
                for (int i = 0; i < array.Length; i++)
                    clone.SetValue(CloneObject(array.GetValue(i)), i);
                return clone;
            }

            if (obj is IList list)
            {
                var cloneList = (IList)Activator.CreateInstance(type);
                foreach (var item in list) cloneList.Add(CloneObject(item));
                return cloneList;
            }

            var copy = Activator.CreateInstance(type);                  // все поведения имеют пустой ctor
            foreach (var field in GetFields(type))
                field.SetValue(copy, CloneObject(field.GetValue(obj)));
            return copy;
        }

        static IEnumerable<FieldInfo> GetFields(Type type)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            for (var cur = type; cur != null && cur != typeof(object); cur = cur.BaseType)
                foreach (var field in cur.GetFields(flags))
                    yield return field;
        }
    }
}
