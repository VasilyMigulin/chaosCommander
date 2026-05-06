namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Разыгрывание карты с руки. Вешается на сущность карты.
    /// </summary>
    public struct CastEvent
    {
        public int OwnerEntity;     // entity игрока, который разыгрывает
        public int TargetEntity;    // -1 если цель не нужна
        public int TargetCell;      // -1 если клетка не нужна
    }
}
