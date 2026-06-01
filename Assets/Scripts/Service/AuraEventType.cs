namespace Game.Core.Service
{
    /// <summary>
    /// Тип события, на которое подписывается реакция ауры.
    /// Расширяется по мере добавления новых триггеров event-listening аур
    /// (Дерзкий расхититель / Зажигалка / Боец на арене и т.д.).
    /// </summary>
    public enum AuraEventType
    {
        /// <summary>Игрок-владелец чары получил урон.</summary>
        OwnerPlayerDamaged    = 0,

        /// <summary>Игрок-владелец чары взял карту в руку.</summary>
        OwnerCardDrawn        = 1,

        /// <summary>Игрок-владелец чары разыграл карту.</summary>
        OwnerCardPlayed       = 2,

        /// <summary>Под контролем владельца погибло существо.</summary>
        OwnerCreatureDied     = 3,

        /// <summary>Под контролем оппонента погибло существо.</summary>
        OpponentCreatureDied  = 4,
    }
}
