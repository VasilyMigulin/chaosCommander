namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Запрос взятия карт из колоды в руку. Вешается на сущность игрока.
    /// Count — сколько карт взять (по умолчанию 1).
    /// </summary>
    public struct DrawCardEvent
    {
        public int Count;

        /// <summary>true — это ЛОКАЛЬНЫЙ добор (turn-start), которого нет у пассива → DrawCardSystem пошлёт
        /// DeckDrawNetEvent для синка (ActionDrawData). false (умолчание) — добор из эффекта (ре-ранится
        /// резолвом на обоих) или сам реплей: синк НЕ нужен, иначе двойной добор.</summary>
        public bool Sync;
    }
}
