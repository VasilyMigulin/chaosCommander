namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Транзитный маркер каскада КОНЦА хода у активного игрока. Пока висит — конец хода в процессе:
    /// подняты OnTurnEnd-способности, ждём оседания (очередь/анимации), затем передаём ход
    /// (снапшот EndTurn + снятие ActiveState).
    /// </summary>
    public struct EndTurnState
    {
        public bool AbilitiesFired;
    }
}
