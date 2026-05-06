using Game.Core.Service; 

namespace Game.Core.Events
{
    // ── Match tracking events ────────────────────────────────────────────────
    // Публикуются из ECS-систем в момент совершения действия.
    // Подписчик: MatchTracker.

    /// <summary>Карта разыграна с руки на поле.</summary>
    public struct CardTrackedEvent : IGameEvent
    {
        public int CardEntity;
        public int ModelId;
        public string CardName;
        public int PlayerId;
        public EnumService.Rarity Rarity;
        public EnumService.Element Element;
        public EnumService.CardType CardType;
    }

    /// <summary>Нанесён урон (от атаки существа или способности).</summary>
    public struct DamageTrackedEvent : IGameEvent
    {
        public int SourceEntity;
        public int TargetEntity;
        public int SourcePlayerId;
        public int TargetPlayerId;
        public int Amount;
    }

    /// <summary>Существо погибло.</summary>
    public struct DeathTrackedEvent : IGameEvent
    {
        public int CreatureEntity;
        public int OwnerId;
        public int KillerPlayerId;  // -1 если неизвестно
        public string CardName;
        public int ModelId;
    }
}
