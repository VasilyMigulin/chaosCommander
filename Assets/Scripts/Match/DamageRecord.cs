namespace Game.Core.Match
{
    /// <summary>
    /// Запись об уроне, нанесённом в матче.
    /// </summary>
    public sealed class DamageRecord
    {
        /// <summary>ECS entity источника урона.</summary>
        public readonly int SourceEntity;

        /// <summary>ECS entity цели.</summary>
        public readonly int TargetEntity;

        /// <summary>Id игрока-источника.</summary>
        public readonly int SourcePlayerId;

        /// <summary>Количество урона.</summary>
        public readonly int Amount;

        /// <summary>Номер хода.</summary>
        public readonly int TurnNumber;

        public DamageRecord(int sourceEntity, int targetEntity, int sourcePlayerId, int amount, int turnNumber)
        {
            SourceEntity   = sourceEntity;
            TargetEntity   = targetEntity;
            SourcePlayerId = sourcePlayerId;
            Amount         = amount;
            TurnNumber     = turnNumber;
        }
    }
}
