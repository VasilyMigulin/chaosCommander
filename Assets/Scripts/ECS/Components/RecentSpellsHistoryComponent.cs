using System.Collections.Generic;

namespace Game.Core.Ecs.Components
{
    // === struct (Component) ===
    /// <summary>
    /// История ПОСЛЕДНИХ разыгранных игроком заклинаний (CardType=Spell), самое свежее — индекс 0.
    /// Обновляется LastPlayedSpellTrackerSystem (рядом с LastPlayedSpellComponent — тот хранит только
    /// самое последнее и занят другими картами, трогать нельзя). Кап — MaxEntries (3 хватает батюшке-барыге).
    /// </summary>
    public struct RecentSpellsHistoryComponent
    {
        public const int MaxEntries = 3;

        public List<string> ExpansionIds;
        public List<int> ModelIds;

        public void Push(string expansionId, int modelId)
        {
            ExpansionIds ??= new List<string>();
            ModelIds ??= new List<int>();
            ExpansionIds.Insert(0, expansionId);
            ModelIds.Insert(0, modelId);
            if (ExpansionIds.Count > MaxEntries)
            {
                ExpansionIds.RemoveRange(MaxEntries, ExpansionIds.Count - MaxEntries);
                ModelIds.RemoveRange(MaxEntries, ModelIds.Count - MaxEntries);
            }
        }
    }
}
