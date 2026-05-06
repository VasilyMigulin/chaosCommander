using Fusion;
using Game.Core.Configs;
using Game.Core.Ecs.Components;
using Game.Core.Ecs.Handlers;
using Game.Core.Events;
using Game.Core.Match;
using Game.Core.Mono;
using Game.Core.Photon;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Game.Core.States
{
    public class BattleState : State, IGameStateContext, IBattleUIContext
    {
        public static new BattleState Instance
        {
            get
            {
                return (BattleState)State.Instance;
            }
        }

        public BoardView BoardView 
        {
            get
            {
                if (_boardView == null) _boardView = FindFirstObjectByType<BoardView>();

                if (_boardView == null) throw new System.NullReferenceException($"{_boardView} not found on scene");

                return _boardView;
            }
        }

        [SerializeField] private BoardView _boardView;
        [SerializeField] private CardConfig _cardConfig;

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
            MatchTracker.Initialize();

            PhotonRunHandler = FindFirstObjectByType<PhotonRunHandler>();

            EcsHandler = EcsRunHandler.Create(this);

            var runner = PhotonInitializer.Instance?.Runner;
             
        }
        public override void Start()
        { 
            EcsHandler.Init(PhotonRunHandler, BoardView, _cardConfig);

            GameEventBus.Subscribe<Game.Core.Events.DeckReadyToSyncEvent>(OnDeckReadyToSync);

            PhotonRunHandler.RPC_NotifyStateReady();
        }

        private void OnDeckReadyToSync(Game.Core.Events.DeckReadyToSyncEvent evt)
        {
            var snapshot = new Game.Core.Photon.NetworkDeckSnapshotData();

            int deckCount = System.Math.Min(evt.DeckNetworkKeys.Length, 30);
            for (int i = 0; i < deckCount; i++)
            {
                snapshot.Deck.Set(i, new Game.Core.Photon.NetworkCardSnapshotEntry
                {
                    ExpansionId = new Fusion.NetworkString<Fusion._32>(evt.DeckExpansionIds[i]),
                    CardId      = evt.DeckCardIds[i],
                    EntityKey   = new Fusion.NetworkString<Fusion._32>(evt.DeckNetworkKeys[i])
                });
            }
            snapshot.DeckCount = deckCount;

            int handCount = System.Math.Min(evt.HandNetworkKeys.Length, 10);
            for (int i = 0; i < handCount; i++)
            {
                snapshot.Hand.Set(i, new Game.Core.Photon.NetworkCardSnapshotEntry
                {
                    ExpansionId = new Fusion.NetworkString<Fusion._32>(evt.HandExpansionIds[i]),
                    CardId      = evt.HandCardIds[i],
                    EntityKey   = new Fusion.NetworkString<Fusion._32>(evt.HandNetworkKeys[i])
                });
            }
            snapshot.HandCount = handCount;

            PhotonRunHandler.RPC_SyncDeckSnapshot(snapshot, evt.PlayerId);
        }

        public override void OnDestroy()
        {
            GameEventBus.Unsubscribe<Game.Core.Events.DeckReadyToSyncEvent>(OnDeckReadyToSync);
            MatchTracker.Shutdown();
        }
        public override void Update() => EcsHandler.Run();
        public void FixedUpdate() => EcsHandler.FixedRun();
        public void LateUpdate() => EcsHandler.LateRun(); 
    }
}