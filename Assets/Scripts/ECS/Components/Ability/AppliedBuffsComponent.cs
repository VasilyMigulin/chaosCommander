using System.Collections.Generic;

namespace Game.Core.Ecs.Components
{
    // === struct (Component) ===
    /// <summary>
    /// Трекинг баффов, выданных РЕАКТИВНОЙ аурой — висит на сущности ИСТОЧНИКА ауры.
    /// `ApplyTrackedBuffEffect` дописывает запись на каждую новую цель (идемпотентно),
    /// `RevertTrackedBuffsEffect` снимает все по трекингу (источник ушёл/умер).
    /// Строится независимо на обоих клиентах (резолв ауры реплеится по тем же целям) → не синкается.
    /// </summary>
    public struct AppliedBuffsComponent
    {
        public List<BuffRecord> Records;
    }

    public struct BuffRecord
    {
        public int Target;
        public int Atk;
        public int Hp;
        public int Speed;
    }
}
