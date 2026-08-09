using System.Collections.Generic;

namespace Game.Core.Ecs.Components
{
    // Модификатор-стек как у GoldCostComponent (см. комментарий там). Cost = эффективное значение.
    public struct HealthCostComponent
    {
        public int Cost;
        public int Base;
        public List<int> Modifiers;
        public List<int> ModifiersPermanent;

        // Принудительный кост (см. GoldCostComponent): установлен → Cost = OverrideValue, стек игнорируется.
        public bool HasOverride;
        public int  OverrideValue;

        public void SetOverride(int value) { HasOverride = true;  OverrideValue = value; RecalculateValue(); }
        public void ClearOverride()        { HasOverride = false; RecalculateValue(); }

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
            if (HasOverride) { Cost = OverrideValue < 0 ? 0 : OverrideValue; return; }
            int v = Base;
            if (Modifiers != null)          for (int i = 0; i < Modifiers.Count; i++)          v += Modifiers[i];
            if (ModifiersPermanent != null) for (int i = 0; i < ModifiersPermanent.Count; i++) v += ModifiersPermanent[i];
            Cost = v < 0 ? 0 : v;
        }

        /// <summary>Форс-установка ПЕЧАТНОЙ базы стоимости (карта-с-уровнями). Устойчиво к ClearModifiers; баффы поверх.</summary>
        public void SetBase(int newBase) { Base = newBase; RecalculateValue(); }
    }
}
