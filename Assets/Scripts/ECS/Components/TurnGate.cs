using Leopotam.EcsLite;

namespace Game.Core.Ecs.Components
{
    // === helper ===
    /// <summary>
    /// Гейт «локальный игрок активен» — его ход. TurnState висит ТОЛЬКО на активном игроке,
    /// поэтому достаточно проверить, локальный ли это игрок (на все фазы хода: TurnStart/PlayerTurn/
    /// TurnEnd). Активный клиент гоняет весь пайплайн (триггеры/правила/таргетинг/резолв) и шлёт
    /// снапшоты; пассивный — только реплей (интерпретация снапшотов).
    /// Лежит в Components, чтобы быть доступным и системам, и поведению способностей (AbilityFire).
    /// </summary>
    public static class TurnGate
    {
        /// <summary>
        /// Локальный игрок активен (его ход — включая каскад НАЧАЛА хода). Активен = у локального
        /// игрока есть ActiveState ИЛИ StartTurnState. StartTurnState нужен, чтобы OnTurnStart-способности
        /// поднимались во время каскада (ActiveState ещё не навешен — его ставит RunActivateSystem
        /// после оседания каскада). На пассиве нет ни того, ни другого → он только реплеит.
        /// </summary>
        public static bool IsLocalActive(EcsWorld world)
        {
            var playerPool = world.GetPool<PlayerComponent>();
            var activePool = world.GetPool<ActiveState>();
            var startPool  = world.GetPool<StartTurnState>();
            var endPool    = world.GetPool<EndTurnState>();
            foreach (var e in world.Filter<PlayerComponent>().End())
            {
                // PvE: сети нет — локальный клиент СИМУЛЯТОР ОБОИХ игроков (и человека, и ИИ).
                // «Активен» = ход у ЛЮБОГО игрока → триггеры/резолв работают и на ходу ИИ.
                if (!Service.PveMode.Enabled && !playerPool.Get(e).IsLocalPlayer) continue;
                // Симулятор хода = активен, ИЛИ идёт каскад начала (StartTurnState), ИЛИ завершение
                // (EndTurnState): на завершении ActiveState уже снят (инпут off), но OnTurnEnd-способности
                // ещё должны сработать и реплей не должен включиться.
                if (activePool.Has(e) || startPool.Has(e) || endPool.Has(e)) return true;
                if (!Service.PveMode.Enabled) return false;   // MP: локальный игрок один — дальше некого смотреть
            }
            return false;
        }
    }
}
