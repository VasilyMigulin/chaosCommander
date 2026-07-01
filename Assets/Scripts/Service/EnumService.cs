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

        [System.Flags]
        public enum Element
        {
            Red = 1 << 0,
            Blue = 1 << 1,
            Green = 1 << 2,
            Yellow = 1 << 3,
            White = 1 << 4,
            Black = 1 << 5
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

            OnMatchStart      = 1 << 10, // at the start of the match

            // Постоянный эффект (аура): действует пока источник (чара/существо) на поле.
            // Не идёт в очередь резолва — регистрирует AuraSourceComponent, пересчитывается AuraRecalcSystem.
            Aura              = 1 << 11,
        }

        // Цели вынесены в единый флаговый enum TargetMask (Game.Core.Service).

        /// <summary>Тип карты для трекинга.</summary>
        public enum CardType { Creature, Spell, Charm }

        /// <summary>Зона нахождения карты/существа. Используется правилами и фильтрами.
        /// Flags чтобы можно было искать в нескольких зонах одним правилом.</summary>
        [System.Flags]
        public enum Zone
        {
            None      = 0,
            Hand      = 1 << 0,
            Deck      = 1 << 1,
            Board     = 1 << 2,
            Graveyard = 1 << 3,
            Banish    = 1 << 4,
            Any       = Hand | Deck | Board | Graveyard | Banish,
        }

        /// <summary>Принадлежность сущности относительно владельца способности.</summary>
        public enum Ownership
        {
            Any,
            Self,
            Enemy,
        }
    }
}
