namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Маркер: существо под ВРЕМЕННЫМ контролем (контроль-на-месте). Актив (контролёр) в конце своего хода
    /// дотикивает TurnsRemaining; при 0 откатывает владельца + теги и шлёт ActionControlRevertData (пассив
    /// повторяет по ключу). Откат считает ТОЛЬКО актив (как таймер-смерти) → пассив сам не тикает.
    /// </summary>
    public struct TempControlledComponent
    {
        public int OriginalOwnerId;
        public bool OriginalWasOwn;   // имел OwnCardTag до захвата (на локальном клиенте)
        public int ExpiresOnPlayerId; // конец чьего хода тикает срок (= контролёр)
        public int TurnsRemaining;    // сколько ходов контролёра ещё держится; 0 → откат
    }
}
