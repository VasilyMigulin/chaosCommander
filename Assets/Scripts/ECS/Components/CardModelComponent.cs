using Game.Core.Service;

namespace Game.Core.Ecs.Components
{ 
    public struct CardModelComponent
    {
        public int ModelId;
        public string ExpansionId;
        public string CardName;
        public EnumService.CardType CardType;
        public EnumService.Rarity Rarity;
        public EnumService.Element Element;
    }
}
