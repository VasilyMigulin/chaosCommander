using System.Collections.Generic;

namespace Game.Core.Ecs.Components
{
    // === struct (Component) ===
    /// <summary>
    /// Архетипы существа — набор ключей ("Imp"/"Worker"/...). Вешает ICreatureTag.Apply на ините карты,
    /// проверяет ICreatureTag.Has, считает MatchCounterTrackerSystem (InvokedByArchetype по этим ключам).
    /// Один компонент вместо per-архетип struct-тегов → новый архетип не требует нового ECS-типа.
    /// </summary>
    public struct ArchetypeComponent
    {
        public List<string> Keys;
    }
}
