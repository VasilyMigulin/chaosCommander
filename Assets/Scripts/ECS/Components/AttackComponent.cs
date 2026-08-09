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

        /// <summary>СТЕК ФИКСАЦИЙ атаки («атака всех существ равна N»): пока стек не пуст, Value = ПОСЛЕДНЕЕ
        /// значение, база и ВСЕ модификаторы игнорируются. Аддитивно: новый фикс поверх старого, снятие
        /// возвращает предыдущий. См. StatOverrideUtil / FixStat.</summary>
        public List<int> Overrides;

        public void AddOverride(int value)
        {
            (Overrides ??= new List<int>()).Add(value);
            RecalculateValue();
        }

        /// <summary>Снять СВОЙ фикс (последнее вхождение значения) — парный к AddOverride.</summary>
        public bool RemoveOverride(int value)
        {
            if (Overrides == null) return false;
            int i = Overrides.LastIndexOf(value);
            if (i < 0) return false;
            Overrides.RemoveAt(i);
            RecalculateValue();
            return true;
        }

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

        /// <summary>Форс-установка ПЕЧАТНОЙ базы (карта-с-уровнями: статы уровня — это новая база, НЕ бафф).
        /// Устойчиво к ClearModifiers (Base не чистится); баффы складываются поверх. Value пересчитывается.</summary>
        public void SetBase(int newBase) { Base = newBase; RecalculateValue(); }

        public void RecalculateValue()
        {
            if (Overrides != null && Overrides.Count > 0)
            {
                int fixedValue = Overrides[Overrides.Count - 1];   // последний фикс — актуальный
                Value = fixedValue < 0 ? 0 : fixedValue;
                return;
            }
            int v = Base;
            if (Modifiers != null)          for (int i = 0; i < Modifiers.Count; i++)          v += Modifiers[i];
            if (ModifiersPermanent != null) for (int i = 0; i < ModifiersPermanent.Count; i++) v += ModifiersPermanent[i];
            Value = v < 0 ? 0 : v;
        }
    }
}
