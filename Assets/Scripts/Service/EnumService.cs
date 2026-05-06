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

        /// <summary>Extensible resource types used for card play costs.</summary>
        public enum ResourceType
        {
            Gold,
            Mana,
            Health,
        }

        /// <summary>Phase of a player's turn.</summary>
        public enum TurnPhase
        {
            WaitingForTurn,
            PlayerInput,
            ResolvingCascade,
            TurnEnd,
        }
        [System.Flags]
        public enum AbilityTrigger
        {
            None           = 0,

            // Creature
            OnCast         = 1 << 0,  // when played from hand
            OnInvoke       = 1 << 1,  // when creature enters the battlefield from anywhere
            OnDie          = 1 << 2,  // on death
            OnAttack       = 1 << 3,  // when attack and deal damage

            // Charm / Aura
            TurnStart      = 1 << 4,
            TurnEnd        = 1 << 5,
            OnAllyDeath    = 1 << 6,
            OnEnemyDeath   = 1 << 7,
            OnAllyPlayed   = 1 << 8,

            // Spell
            OnDrawn        = 1 << 9,  // received into hand
        }

        [System.Flags]
        public enum AbilityTarget 
        {
            None   = 0,
            Self   = 1 << 0,
            Enemy  = 1 << 1,
            Ally   = 1 << 2,
            Player = 1 << 3,

            All    = Self | Enemy | Ally | Player,
        }


        /// <summary>Тип карты для трекинга.</summary>
        public enum CardType { Creature, Spell, Charm }
    }
}
