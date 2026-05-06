using System; 
using System.Collections.Generic;
using System.Reflection;

namespace AwesomeUI.Service
{
    public static class UnjectExtensions
    {
        private static readonly Dictionary<Type, MethodInfo> _cachedMethods = new();

        public static void UninjectAll<T>(this List<T> list)
        {
            if (list == null || list.Count == 0)
                return;

            foreach (var item in list)
            {
                if (item == null)
                    continue;

                var type = item.GetType();

                // кешируем метод чтобы не искать его каждый раз
                if (!_cachedMethods.TryGetValue(type, out var method))
                {
                    method = type.GetMethod("Unject", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    _cachedMethods[type] = method;
                }

                method?.Invoke(item, null);
            }
        }
    }
}