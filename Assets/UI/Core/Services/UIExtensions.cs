using System;
using System.Collections.Generic;
using System.Reflection;
using AwesomeUI.Core.Attributes;
using UnityEngine;

namespace AwesomeUI.Core.Service
{
    public static class UIExtensions
    {
        /// <summary>
        /// Очищает все [UIInject] поля в списке объектов
        /// </summary>
        public static void UninjectAll<T>(this List<T> targets) where T : class
        {
            foreach (var target in targets)
            {
                if (target == null)
                    continue;

                var fields = target.GetType()
                    .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                foreach (var field in fields)
                {
                    if (field.GetCustomAttribute<UIInjectAttribute>() != null)
                    {
                        try
                        {
                            field.SetValue(target, null);
                        }
                        catch (Exception e)
                        {
                            Debug.LogError($"[UIExtensions] Error uninjecting {target.GetType().Name}.{field.Name}: {e.Message}");
                        }
                    }
                }

                // Вызываем Unject если есть
                var unjectMethod = target.GetType().GetMethod("Unject", BindingFlags.Public | BindingFlags.Instance);
                unjectMethod?.Invoke(target, null);
            }
        }
    }
}