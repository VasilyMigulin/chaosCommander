using Fusion;

namespace Game.Core.Photon
{
    /// <summary>
    /// Данные для RPC_CreatureCast.
    /// Содержит всё необходимое для детерминированного воспроизведения на обеих сторонах.
    /// </summary>
    public struct NetworkCreatureCastData : INetworkStruct
    {
        public NetworkString<_64> CardNetworkKey;   // NetworkEntityComponent.NetworkEntityKey карты
        public int TargetCellIndex;                 // 0-9 (row * 5 + col в пределах стороны игрока)
    }

    /// <summary>
    /// Данные для RPC_SpellCast.
    /// </summary>
    public struct NetworkSpellCastData : INetworkStruct
    {
        public NetworkString<_64> CardNetworkKey;
        public NetworkString<_64> TargetNetworkKey; // NetworkEntityComponent.NetworkEntityKey цели (-1 если нет)
        public int TargetCellIndex;                 // -1 если цель не клетка
    }

    /// <summary>
    /// Данные для RPC_CharmCast.
    /// </summary>
    public struct NetworkCharmCastData : INetworkStruct
    {
        public NetworkString<_64> CardNetworkKey;
        public NetworkString<_64> TargetNetworkKey;
    }

    /// <summary>
    /// Данные для RPC_EndTurn.
    /// </summary>
    public struct NetworkEndTurnData : INetworkStruct
    {
        public int ActorNumber;
        public int TurnNumber;
    }

    /// <summary>
    /// Снэпшот колоды + руки, отправляемый оппоненту в начале матча.
    /// Хранит ExpansionId + CardId для восстановления CardModel через CardConfig,
    /// и EntityKey для привязки к уже созданным локальным entity.
    /// </summary>
    public struct NetworkDeckSnapshotData : INetworkStruct
    {
        [Capacity(30)] public NetworkArray<NetworkCardSnapshotEntry> Deck { get; }
        public int DeckCount;
        [Capacity(10)] public NetworkArray<NetworkCardSnapshotEntry> Hand { get; }
        public int HandCount;
    }

    public struct NetworkCardSnapshotEntry : INetworkStruct
    {
        public NetworkString<_32> ExpansionId; // ExpansionConfig.ExpansionId
        public int CardId;                     // CardModel.Id
        public NetworkString<_32> EntityKey;   // NetworkEntityComponent.NetworkEntityKey
    }
}
