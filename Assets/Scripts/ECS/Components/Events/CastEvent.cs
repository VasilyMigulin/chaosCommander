using Game.Core.Service;

namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Разыгрывание карты с руки. Вешается на сущность карты.
    /// </summary>
    public struct CastEvent
    { 
        public EnumService.CardType CardType; // тип карты, которая разыгрывается
        public int CardEntity;      // сущность карты, которая разыгрывается    
        public int PlayerOwnerEntity;     // entity игрока, который разыгрывает
        public int TargetEntity;    // -1 если цель не нужна
        public int TargetCell;      // -1 если клетка не нужна
    }
}
