using System.Collections.Generic;

namespace Game.Core.Ecs.Components
{
    // ── Creature stats ───────────────────────────────────────────────────────
    public struct HealthComponent
    {
        /// <summary>Текущее здоровье.</summary>
        public int Current;

        /// <summary>Эффективный максимум = BaseMax + Σ Modifiers + Σ ModifiersPermanent. Пересчёт — RecalculateValue.</summary>
        public int Max;

        /// <summary>Базовый (печатный) максимум. ИММУТАБЕЛЕН: ставится только при инициализации, эффекты его не трогают.</summary>
        public int BaseMax;

        /// <summary>МЯГКИЕ баффы HP: ауры и «мягкий перм». ЧИСТЯТСЯ ПРИ СМЕРТИ (ClearModifiers).</summary>
        public List<int> Modifiers;

        /// <summary>ИСТИННО ПЕРМАНЕНТНЫЕ баффы HP: не чистятся даже при смерти.</summary>
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
            int v = BaseMax;
            if (Modifiers != null)          for (int i = 0; i < Modifiers.Count; i++)          v += Modifiers[i];
            if (ModifiersPermanent != null) for (int i = 0; i < ModifiersPermanent.Count; i++) v += ModifiersPermanent[i];
            Max = v < 0 ? 0 : v;
            if (Current > Max) Current = Max;   // снятие HP-модификатора могло понизить максимум
            if (Current < 0)  Current = 0;
        }
    }
}
