using Fusion;
using UnityEngine;

namespace Game.Core.Photon
{
    public struct NetworkSessionData : INetworkStruct
    { 
        public NetworkString<_128> ScenePath;
        public int TargetPlayerCount;
        public int Seed;
    }
}