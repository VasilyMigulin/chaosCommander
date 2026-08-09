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

        /// <summary>СТЕК ФИКСАЦИЙ максимума HP («здоровье всех существ равно N»): пока стек не пуст,
        /// Max = ПОСЛЕДНЕЕ значение, база и ВСЕ модификаторы игнорируются. Аддитивно: новый фикс поверх
        /// старого, снятие возвращает предыдущий. Current клампится сверху (как при усадке Max).
        /// См. StatOverrideUtil / FixStat.</summary>
        public List<int> Overrides;

        public void AddOverride(int value)
        {
            (Overrides ??= new List<int>()).Add(value);
            int oldMax = Max;
            RecalculateValue();
            if (Max > oldMax) Current += Max - oldMax;   // рост фикса лечит на дельту (как SetBaseMax)
        }

        /// <summary>Снять СВОЙ фикс (последнее вхождение значения) — парный к AddOverride.</summary>
        public bool RemoveOverride(int value)
        {
            if (Overrides == null) return false;
            int i = Overrides.LastIndexOf(value);
            if (i < 0) return false;
            Overrides.RemoveAt(i);
            int oldMax = Max;
            RecalculateValue();
            if (Max > oldMax) Current += Max - oldMax;
            return true;
        }

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

        /// <summary>Форс-установка ПЕЧАТНОГО максимума (карта-с-уровнями: HP уровня — новая база, НЕ бафф). Устойчиво
        /// к ClearModifiers. При РОСТЕ базы Current тянется за Max (существо здоровеет); при снижении RecalculateValue
        /// клампит Current. Полное восстановление при возврате в руку/из кладбища делает ResetStats (Current=Max).</summary>
        public void SetBaseMax(int newBaseMax)
        {
            int oldMax = Max;
            BaseMax = newBaseMax;
            RecalculateValue();
            if (Max > oldMax) Current += Max - oldMax;
        }

        public void RecalculateValue()
        {
            int v;
            if (Overrides != null && Overrides.Count > 0)
                v = Overrides[Overrides.Count - 1];   // последний фикс — актуальный, база/модификаторы игнорируются
            else
            {
                v = BaseMax;
                if (Modifiers != null)          for (int i = 0; i < Modifiers.Count; i++)          v += Modifiers[i];
                if (ModifiersPermanent != null) for (int i = 0; i < ModifiersPermanent.Count; i++) v += ModifiersPermanent[i];
            }
            Max = v < 0 ? 0 : v;
            if (Current > Max) Current = Max;   // снятие HP-модификатора могло понизить максимум
            if (Current < 0)  Current = 0;
        }
    }
}
