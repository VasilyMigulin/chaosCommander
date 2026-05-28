using UnityEngine;

namespace Game.Core.Ecs.Components
{
    public struct ViewRefComponent
    {
        /// <summary>Исходный префаб для (ре)спавна визуала (нужен при повторном развёртывании командира).</summary>
        public GameObject Prefab;

        /// <summary>Текущий инстанс на сцене. null — визуал ещё не создан / уничтожен.</summary>
        public GameObject View;
    }
}