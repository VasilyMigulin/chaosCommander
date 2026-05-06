using Fusion;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Photon;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using UnityEngine;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Создаёт entity локального игрока.
    /// PlayerId = Photon ActorNumber.
    /// Side = 1 если ActorNumber наименьший среди подключённых, иначе 2.
    /// Вешает GoldComponent, ManaComponent, DeckComponent, HandComponent.
    /// </summary>
    public sealed class InitLocalPlayerSystem : IEcsInitSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsSharedInject<IGameStateContext> _state = default;
        readonly EcsCustomInject<PhotonRunHandler> _handler = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsPoolInject<PlayerSideComponent> _sidePool = default;
        readonly EcsPoolInject<GoldComponent> _goldPool = default;
        readonly EcsPoolInject<ManaComponent> _manaPool = default;
        readonly EcsPoolInject<DeckComponent> _deckPool = default;
        readonly EcsPoolInject<HandComponent> _handPool = default;

        public void Init(IEcsSystems systems)
        {  
            var runner = PhotonInitializer.Instance?.Runner;
            if (runner == null)
            {
                Debug.LogError("[InitLocalPlayerSystem] NetworkRunner not found");
                return;
            }

            int localActorNumber = runner.LocalPlayer.PlayerId;
            int side = DetermineLocalSide(runner, localActorNumber);

            int entity = _world.Value.NewEntity();

            ref var player = ref _playerPool.Value.Add(entity);
            player.PlayerId = localActorNumber;
            player.IsLocalPlayer = true;

            ref var playerSide = ref _sidePool.Value.Add(entity);
            playerSide.Side = side;

            ref var gold = ref _goldPool.Value.Add(entity);
            gold.Current = 0;
            gold.Max = 10;

            ref var mana = ref _manaPool.Value.Add(entity);
            mana.Current = 0;
            mana.Max = 10;

            ref var deck = ref _deckPool.Value.Add(entity);
            deck.CardEntities = new int[0];
            deck.Count        = 0;

            ref var hand = ref _handPool.Value.Add(entity);
            hand.CardEntities = new int[HandComponent.MaxHandSize];
            hand.Count = 0;

            _state.Value.AddEntity(entity, "player");

            GameEventBus.Publish(new PlayerAssignedEvent
            {
                PlayerEntity = entity,
                Side = side,
                IsLocalPlayer = true
            });

            Debug.Log($"[InitLocalPlayerSystem] Local player entity={entity} playerId={localActorNumber} side={side}");
        }

        private static int DetermineLocalSide(NetworkRunner runner, int localActorNumber)
        {
            int minActorNumber = int.MaxValue;
            foreach (var p in runner.ActivePlayers)
            {
                if (p.PlayerId < minActorNumber)
                    minActorNumber = p.PlayerId;
            }
            return localActorNumber == minActorNumber ? 1 : 2;
        }
    }
}
