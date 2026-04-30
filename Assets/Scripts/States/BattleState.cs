using Fusion;
using Game.Core.Ecs.Handlers;
using Game.Core.Photon;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Game.Core.States
{
    public class BattleState : State, IGameStateContext
    {
        public static new BattleState Instance
        {
            get
            {
                return (BattleState)State.Instance;
            }
        }

        [HideInInspector] public EcsRunHandler EcsHandler;
        [HideInInspector] public PhotonRunHandler PhotonRunHandler;
         
        protected Dictionary<string, EcsPackedEntity> _localKeyMap = new();
        protected Dictionary<string, EcsPackedEntity> _netKeyMap = new();
        protected Dictionary<int, string> _netLocalMap = new();
        protected Dictionary<int, (PlayerRef playerRef, NetworkPlayerData playerData)> dictionaryPlayers = new Dictionary<int, (PlayerRef, NetworkPlayerData)>();
        public bool IsServer => PhotonRunHandler.IsServer;
        public EcsWorld World => EcsHandler.World;

        public void AddEntity(int entity, string localKey = null, string networkKey = null)
        {
            var packed = EcsHandler.World.PackEntity(entity);

            if (!string.IsNullOrEmpty(localKey) && !_localKeyMap.ContainsKey(localKey))
                _localKeyMap[localKey] = packed;

            if (!string.IsNullOrEmpty(networkKey) && !_netKeyMap.ContainsKey(networkKey))
                _netKeyMap[networkKey] = packed;

            if (!_netLocalMap.ContainsKey(entity))
            {
                _netLocalMap[entity] = networkKey;
            }
        }

        public string GetNetEntityKey(int entity)
        {
            return _netLocalMap.TryGetValue(entity, out var key) ? key : null;
        }

        public bool TryGetPlayer(out int playerEntity)
        {
            return TryGetEntity("player", out playerEntity);
        }

        public bool TryGetEntity(string key, out int entity)
        {
            if (TryGetEntity(key, out EcsPackedEntity packed) && packed.Unpack(EcsHandler.World, out entity))
            {
                return true;
            }

            entity = -1;
            return false;
        }

        bool TryGetEntity(string key, out EcsPackedEntity packedEntity)
        {
            packedEntity = default;

            if (string.IsNullOrEmpty(key)) return false;

            return _localKeyMap.TryGetValue(key, out packedEntity) || _netKeyMap.TryGetValue(key, out packedEntity);
        }
        public override void Awake()
        {
            PhotonRunHandler = FindFirstObjectByType<PhotonRunHandler>();

            EcsHandler = EcsRunHandler.Create(this);

            var runner = PhotonInitializer.Instance?.Runner;
             
        }
        public override void Start()
        {
            EcsHandler.Init(PhotonRunHandler); 

            PhotonRunHandler.RPC_NotifyStateReady();
        }
        public override void Update() => EcsHandler.Run();
        public override void FixedUpdate() => EcsHandler.FixedRun();
        public override void LateUpdate() => EcsHandler.LateRun(); 
    }
}