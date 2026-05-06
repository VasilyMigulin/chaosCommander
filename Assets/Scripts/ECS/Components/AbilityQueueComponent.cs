using System.Collections.Generic;

namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// LIFO-стек способностей ожидающих резолва.
    /// Лежит на singleton-сущности с AbilityQueueTag.
    /// </summary>
    public struct AbilityQueueComponent
    {
        public Stack<int> Abilities;

        public void Push(int abilityEntity)
        {
            if (Abilities == null) Abilities = new Stack<int>();
            Abilities.Push(abilityEntity);
        }

        public bool TryPop(out int abilityEntity)
        {
            if (Abilities != null && Abilities.Count > 0)
            {
                abilityEntity = Abilities.Pop();
                return true;
            }
            abilityEntity = -1;
            return false;
        }

        public bool IsEmpty => Abilities == null || Abilities.Count == 0;
    }
}
