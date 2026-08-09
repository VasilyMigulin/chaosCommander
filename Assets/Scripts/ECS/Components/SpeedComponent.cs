using System.Collections.Generic;

namespace Game.Core.Ecs.Components
{
    /// <summary>How many actions (move / attack) the creature may perform per turn.</summary>
    public struct SpeedComponent
    {
        /// <summary>Печатная база скорости. ИММУТАБЕЛЬНА: ставится только при инициализации, эффекты её не трогают.</summary>
        public int BaseMax;

        /// <summary>Эффективный максимум = BaseMax + Σ Modifiers + Σ ModifiersPermanent. Пересчёт — RecalculateValue.</summary>
        public int Max;

        /// <summary>Оставшийся бюджет действий в текущем ходу.</summary>
        public int Remaining;

        /// <summary>МЯГКИЕ баффы скорости: ауры и «мягкий перм». ЧИСТЯТСЯ ПРИ СМЕРТИ (ClearModifiers).</summary>
        public List<int> Modifiers;

        /// <summary>ИСТИННО ПЕРМАНЕНТНЫЕ баффы скорости: не чистятся даже при смерти.</summary>
        public List<int> ModifiersPermanent;

        /// <summary>СТЕК ФИКСАЦИЙ («скорость всех существ равна 0» — Придворный этикет): пока стек не пуст,
        /// Max = ПОСЛЕДНЕЕ значение, а база и ВСЕ модификаторы игнорируются. Накладываются аддитивно:
        /// новый фикс поверх старого (последний актуален), снятие возвращает предыдущий, пустой стек —
        /// обычный расчёт. См. StatOverrideUtil / FixStat.</summary>
        public List<int> Overrides;

        public void AddOverride(int value)
        {
            (Overrides ??= new List<int>()).Add(value);
            int oldMax = Max;
            RecalculateValue();
            if (Max > oldMax) Remaining += Max - oldMax;
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
            if (Max > oldMax) Remaining += Max - oldMax;
            return true;
        }

        public void AddModifier(int m, bool permanent = false)
        {
            if (permanent) (ModifiersPermanent ??= new List<int>()).Add(m);
            else           (Modifiers          ??= new List<int>()).Add(m);

            // Прирост Max сразу превращаем в юзабельный Remaining ЭТОТ же ход — иначе бафф, навешанный триггером
            // «в начале хода» (напр. «За работу!»), приходил бы визуально на ход позже: RunTurnStartSystem уже
            // сбросил Remaining=Max (СТАРЫЙ) ДО того, как TurnStartedEvent доходит до OnTurnStartTrigger и
            // резолвит бафф — RecalculateValue сам поднять Remaining не мог (только подрезал сверху).
            int oldMax = Max;
            RecalculateValue();
            if (Max > oldMax) Remaining += Max - oldMax;
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

        /// <summary>Форс-установка ПЕЧАТНОЙ базы скорости (карта-с-уровнями). Устойчиво к ClearModifiers; при росте
        /// базы Remaining тянется за Max в этот же ход, при снижении RecalculateValue клампит.</summary>
        public void SetBaseMax(int newBaseMax)
        {
            int oldMax = Max;
            BaseMax = newBaseMax;
            RecalculateValue();
            if (Max > oldMax) Remaining += Max - oldMax;
        }

        public void RecalculateValue()
        {
            if (Overrides != null && Overrides.Count > 0)
            {
                int fixedValue = Overrides[Overrides.Count - 1];   // последний фикс — актуальный
                Max = fixedValue < 0 ? 0 : fixedValue;
                if (Remaining > Max) Remaining = Max;
                return;
            }
            int v = BaseMax;
            if (Modifiers != null)          for (int i = 0; i < Modifiers.Count; i++)          v += Modifiers[i];
            if (ModifiersPermanent != null) for (int i = 0; i < ModifiersPermanent.Count; i++) v += ModifiersPermanent[i];
            Max = v < 0 ? 0 : v;
            if (Remaining > Max) Remaining = Max;
        }
    }
}
