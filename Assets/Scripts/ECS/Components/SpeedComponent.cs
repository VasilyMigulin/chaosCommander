namespace Game.Core.Ecs.Components
{
    /// <summary>How many actions (move / attack) the creature may perform per turn.</summary>
    public struct SpeedComponent
    {
        /// <summary>Печатная база скорости (никогда не модифицируется аурами).</summary>
        public int BaseMax;
        /// <summary>Эффективный максимум = BaseMax + Σ скорости-бонусов от аур. Пересчитывает AuraRecalcSystem.</summary>
        public int Max;
        /// <summary>Оставшийся бюджет действий в текущем ходу.</summary>
        public int Remaining;
    }
}