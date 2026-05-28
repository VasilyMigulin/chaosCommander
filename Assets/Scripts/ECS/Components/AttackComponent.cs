namespace Game.Core.Ecs.Components
{
    public struct AttackComponent
    {
        /// <summary>Эффективная атака = Base + сумма активных аур. Читается боем и UI.</summary>
        public int Value;

        /// <summary>Базовая атака: печатная + перманентные баффы. Ауры её не трогают.</summary>
        public int Base;
    }
}