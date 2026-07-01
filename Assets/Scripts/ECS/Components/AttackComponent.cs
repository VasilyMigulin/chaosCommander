using System.Collections.Generic;

namespace Game.Core.Ecs.Components
{
    public struct AttackComponent
    {
        /// <summary>Эффективная атака = Base + Σ Modifiers + Σ ModifiersPermanent. Читается боем/UI. Пересчёт — RecalculateValue.</summary>
        public int Value;

        /// <summary>Базовая (печатная) атака. ИММУТАБЕЛЬНА: ставится только при инициализации, эффекты её не трогают.</summary>
        public int Base;

        /// <summary>МЯГКИЕ баффы: ауры (source реверт) и «мягкий перм» (напр. Тайный воздыхатель).
        /// ЧИСТЯТСЯ ПРИ СМЕРТИ существа (ClearModifiers).</summary>
        public List<int> Modifiers;

        /// <summary>ИСТИННО ПЕРМАНЕНТНЫЕ баффы: НЕ чистятся даже при смерти (переживают возврат/ресуммон).</summary>
        public List<int> ModifiersPermanent;

        /// <summary>Добавить бафф. permanent=true → в ModifiersPermanent (не снимается, переживает смерть).</summary>
        public void AddModifier(int m, bool permanent = false)
        {
            if (permanent) (ModifiersPermanent ??= new List<int>()).Add(m);
            else           (Modifiers          ??= new List<int>()).Add(m);
            RecalculateValue();
        }

        /// <summary>Снять бафф из МЯГКОГО списка (ауры/временные). Перманентные не трогает.</summary>
        public bool RemoveModifier(int m)
        {
            bool r = Modifiers != null && Modifiers.Remove(m);
            if (r) RecalculateValue();
            return r;
        }

        /// <summary>При смерти: чистим МЯГКИЕ модификаторы, перманентные оставляем.</summary>
        public void ClearModifiers()
        {
            if (Modifiers != null && Modifiers.Count > 0) { Modifiers.Clear(); RecalculateValue(); }
        }

        public void RecalculateValue()
        {
            int v = Base;
            if (Modifiers != null)          for (int i = 0; i < Modifiers.Count; i++)          v += Modifiers[i];
            if (ModifiersPermanent != null) for (int i = 0; i < ModifiersPermanent.Count; i++) v += ModifiersPermanent[i];
            Value = v < 0 ? 0 : v;
        }
    }
}
