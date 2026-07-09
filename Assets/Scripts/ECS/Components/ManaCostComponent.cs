using System.Collections.Generic;

namespace Game.Core.Ecs.Components
{
    // Модификатор-стек как у GoldCostComponent (см. комментарий там). Cost = эффективное значение.
    public struct ManaCostComponent
    {
        public int Cost;
        public int Base;
        public List<int> Modifiers;
        public List<int> ModifiersPermanent;

        public void AddModifier(int m, bool permanent = false)
        {
            if (permanent) (ModifiersPermanent ??= new List<int>()).Add(m);
            else           (Modifiers          ??= new List<int>()).Add(m);
            RecalculateValue();
        }

        public bool RemoveModifier(int m)
        {
            bool r = Modifiers != null && Modifiers.Remove(m);
            if (r) RecalculateValue();
            return r;
        }

        public void ClearModifiers()
        {
            if (Modifiers != null && Modifiers.Count > 0) { Modifiers.Clear(); RecalculateValue(); }
        }

        public void RecalculateValue()
        {
            int v = Base;
            if (Modifiers != null)          for (int i = 0; i < Modifiers.Count; i++)          v += Modifiers[i];
            if (ModifiersPermanent != null) for (int i = 0; i < ModifiersPermanent.Count; i++) v += ModifiersPermanent[i];
            Cost = v < 0 ? 0 : v;
        }
    }
}
