namespace Game.Core.Ecs.Components
{
    // === struct (State, рантайм-прогресс цепочки) ===
    /// <summary>
    /// Прогресс выполнения составной способности. Ставит RunCheckAbilityRulesSystem при старте цепочки,
    /// ведёт RunChainSystem, снимает по завершении всех стадий.
    /// </summary>
    public struct ChainStateComponent
    {
        /// <summary>Текущая стадия (индекс в AbilityChainComponent.Stages).</summary>
        public int Current;
        /// <summary>Стадия уже применена и мы ждём оседания мира (урон/смерти) перед шагом дальше.</summary>
        public bool Applied;
        /// <summary>Накоплено погибших среди целей пройденных стадий (контекст для CountSource).</summary>
        public int Killed;
        /// <summary>Цели текущей применённой стадии — по ним считаем смерти после оседания.</summary>
        public int[] LastTargets;
    }
}
