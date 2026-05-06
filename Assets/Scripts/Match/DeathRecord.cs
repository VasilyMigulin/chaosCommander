namespace Game.Core.Match
{
    /// <summary>
    /// Запись о гибели существа в матче.
    /// </summary>
    public sealed class DeathRecord
    {
        /// <summary>ECS entity погибшего существа.</summary>
        public readonly int CreatureEntity;

        /// <summary>Id владельца погибшего.</summary>
        public readonly int OwnerId;

        /// <summary>Имя карты существа.</summary>
        public readonly string CardName;

        /// <summary>Model.Id существа.</summary>
        public readonly int ModelId;

        /// <summary>Номер хода.</summary>
        public readonly int TurnNumber;

        public DeathRecord(int creatureEntity, int ownerId, string cardName, int modelId, int turnNumber)
        {
            CreatureEntity = creatureEntity;
            OwnerId        = ownerId;
            CardName       = cardName;
            ModelId        = modelId;
            TurnNumber     = turnNumber;
        }
    }
}
