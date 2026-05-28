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

        [System.Flags]
        public enum AbilityTarget 
        {
            None            = 0,

            /// <summary>Сама карта-источник.</summary>
            Self            = 1 << 0,

            /// <summary>Существа противника на поле.</summary>
            EnemyCreature   = 1 << 1,

            /// <summary>Союзные существа на поле.</summary>
            AllyCreature    = 1 << 2,

            /// <summary>Свой игрок (герой).</summary>
            AllyPlayer      = 1 << 3,

            /// <summary>Игрок противника (герой).</summary>
            EnemyPlayer     = 1 << 4,

            /// <summary>Если установлен — бьёт ВСЕХ подходящих (field), иначе первого детерминированно.</summary>
            Field           = 1 << 5,

            /// <summary>Цель выбирается случайно из подходящих. Seed детерминирован — оба клиента получат одну цель.</summary>
            Random          = 1 << 6,

            /// <summary>Исключает самого кастера из пула целей (актуально для AllyCreature / All).</summary>
            ExcludeSelf     = 1 << 7,

            // ── удобные комбо ─────────────────────────────────────────────
            AllEnemies      = EnemyCreature | EnemyPlayer,
            AllAllies       = AllyCreature | AllyPlayer,
            AllCreatures    = EnemyCreature | AllyCreature,
            All             = EnemyCreature | AllyCreature | AllyPlayer | EnemyPlayer | Self,

            /// <summary>Случайный враг (существо или герой).</summary>
            RandomEnemy     = AllEnemies | Random,

            /// <summary>Случайное союзное существо, кроме самого кастера.</summary>
            RandomAlly      = AllyCreature | Random | ExcludeSelf,
        }


        /// <summary>Тип карты для трекинга.</summary>
        public enum CardType { Creature, Spell, Charm }
    }
}
