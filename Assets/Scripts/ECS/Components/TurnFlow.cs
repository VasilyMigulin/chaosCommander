using Leopotam.EcsLite;

namespace Game.Core.Ecs.Components
{
    // === helper ===
    /// <summary>
    /// Выдача хода игроку: инкремент личного счётчика + установка StartTurnState (запуск каскада
    /// начала хода). ActiveState навесит RunActivateSystem, когда каскад осядет. Используется
    /// и при первом ходе (PhotonRunHandler), и при передаче (ReplayActionSystem на ActionEndTurnData).
    /// </summary>
    public static class TurnFlow
    {
        public static void GrantTurn(EcsWorld world, int playerEntity, int globalTurnNumber)
        {
            if (MatchState.IsOver) return;   // матч окончен — ход не выдаём

            var counterPool = world.GetPool<TurnCounterComponent>();
            if (!counterPool.Has(playerEntity)) counterPool.Add(playerEntity);
            ref var c = ref counterPool.Get(playerEntity);
            c.Personal++;

            var startPool = world.GetPool<StartTurnState>();
            if (!startPool.Has(playerEntity)) startPool.Add(playerEntity);
            ref var st = ref startPool.Get(playerEntity);
            st.Resolved = false;
            st.PersonalTurnNumber = c.Personal;
            st.TurnNumber = globalTurnNumber;
        }
    }
}
