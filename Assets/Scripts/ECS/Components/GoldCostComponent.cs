using System.Collections.Generic;

namespace Game.Core.Ecs.Components
{
    // Модификатор-стек как у AttackComponent (решение 2026-07-04: стоимость = бафф — перм/мягкий, стакается,
    // снимается). Имя поля Cost СОХРАНЕНО как эффективное значение → читатели (оплата/affordability/пики/UI)
    // не тронуты. Глобальный модификатор игрока (Гиперинфляция, CostModifierUtil) — ПОВЕРХ этого, отдельный слой.
    public struct GoldCostComponent
    {
        /// <summary>Эффективная стоимость = Base + Σ Modifiers + Σ ModifiersPermanent (clamp 0).</summary>
        public int Cost;

        /// <summary>Печатная стоимость. ИММУТАБЕЛЬНА: ставится при инициализации (CardModel.Init), эффекты её не трогают.</summary>
        public int Base;

        /// <summary>МЯГКИЕ кост-баффы (снимаемые/ауры). Чистятся при смерти существа (DieSystem), как у статов.</summary>
        public List<int> Modifiers;

        /// <summary>ИСТИННО ПЕРМАНЕНТНЫЕ кост-баффы (скидка дискавера/Обнести хату) — переживают смерть.</summary>
        public List<int> ModifiersPermanent;

        /// <summary>ПРИНУДИТЕЛЬНЫЙ кост («стоит N, пока условие» — Запойное время): пока установлен,
        /// Cost = OverrideValue, база и ВЕСЬ стек модификаторов игнорируются; снятие возвращает обычный
        /// расчёт. Глобальный модификатор игрока (Гиперинфляция) — по-прежнему ПОВЕРХ (отдельный слой).</summary>
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
