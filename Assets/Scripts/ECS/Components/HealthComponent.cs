namespace Game.Core.Ecs.Components
{
    // ── Creature stats ───────────────────────────────────────────────────────
    public struct HealthComponent
    {
        public int Current;

        /// <summary>Эффективный максимум = BaseMax + сумма активных аур по HP.</summary>
        public int Max;

        /// <summary>Базовый максимум: печатный + перманентные баффы. Ауры его не трогают.</summary>
        public int BaseMax;
    }
}