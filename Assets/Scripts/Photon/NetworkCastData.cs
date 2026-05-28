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

}
