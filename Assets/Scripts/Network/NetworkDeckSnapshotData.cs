using MemoryPack;
using UnityEngine;

namespace Game.Core.Network
{
    /// <summary>
    /// Снэпшот колоды + руки, отправляемый оппоненту в начале матча.
    /// Хранит ExpansionId + CardId для восстановления CardModel через CardConfig,
    /// и EntityKey для привязки к уже созданным локальным entity.
    /// </summary>
    [MemoryPackable]
    public partial struct NetworkDeckSnapshotData
    {
        public NetworkCardSnapshotEntry Commander;
        public NetworkCardSnapshotEntry[] Deck;
        public int DeckCount;
        public NetworkCardSnapshotEntry[] Hand;
        public int HandCount;
    }

    [MemoryPackable]
    public partial struct NetworkCardSnapshotEntry
    {
        public string ExpansionId; // ExpansionConfig.ExpansionId
        public int CardId;                     // CardModel.Id
        public string EntityKey;   // NetworkEntityComponent.NetworkEntityKey
    }
}