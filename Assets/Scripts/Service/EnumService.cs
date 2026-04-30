using UnityEngine;

namespace Game.Core.Service
{
    public static class EnumService
    {
        public enum Rarity 
        {
            Common, 
            Rare,
            Epic,
            Legendary,
            Exotic  
        }

        public enum Element
        {
            Red,
            Blue,
            Green,
            Yellow,
            White,
            Black
        }
    }
}