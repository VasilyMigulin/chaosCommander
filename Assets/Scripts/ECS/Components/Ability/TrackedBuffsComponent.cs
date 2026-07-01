using System.Collections.Generic;
using Game.Core.Shared.Interface;

namespace Game.Core.Ecs.Components
{
    // === struct (Component) ===
    /// <summary>
    /// Трекинг баффов, выданных аурой ИСТОЧНИКА (AddBuffEffect{Tracked}). При снятии (источник погиб)
    /// откатывается по списку: для каждого buff.Revert(target). Хранит IBuffable (Shared.Interface) — поэтому
    /// компонент тут, а не в Ability (Ecs.Components ссылается на Shared.Interface, не наоборот).
    /// Строится зеркально на обоих (резолв ауры реплеится по тем же целям) → не синкается.
    /// </summary>
    public struct TrackedBuffsComponent
    {
        public List<TrackedBuff> Items;
    }

    public struct TrackedBuff
    {
        public int Target;
        public IBuffable Buff;
    }
}
