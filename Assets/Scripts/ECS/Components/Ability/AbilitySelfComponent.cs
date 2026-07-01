namespace Game.Core.Ecs.Components
{
    // === struct (Component) ===
    /// <summary>
    /// Маркер: цель способности — САМ источник (caster cardEntity). Ставит подтип AbilityToSelf.
    /// RunAbilityTargetingSystem выдаёт targets = [owner.CardEntity]. Для самобаффов/самолечения
    /// (Боец на арене «+1 скорость в начале хода», Грыз «+1/1 за каждого чёрта»).
    /// </summary>
    public struct AbilitySelfComponent { }
}
