namespace Game.Core.Ecs.Components
{
    /// <summary>Сколько раз этот игрок начинал ход (для дохода золота). Растёт при выдаче хода.</summary>
    public struct TurnCounterComponent
    {
        public int Personal;
    }
}
