using System.Collections.Generic;

namespace Game.Core.Service
{
    public static class ListExtension
    {
        /// <summary>
        /// Перемешивает список на месте алгоритмом Фишера-Йетса.
        /// Использует UnityEngine.Random для совместимости с Unity.
        /// </summary>
        public static void Shuffle<T>(this List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}