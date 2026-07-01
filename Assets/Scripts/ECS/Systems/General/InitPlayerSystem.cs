using Fusion;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Mono;
using Game.Core.Photon;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using System.Linq;
using UnityEngine;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Создаёт entity локального игрока.
    /// PlayerId = Photon ActorNumber.
    /// Side = 1 если ActorNumber наименьший среди подключённых, иначе 2.
    /// Вешает GoldComponent, ManaComponent, DeckComponent, HandComponent.
    /// </summary>
    public sealed class InitPlayerSystem : IEcsInitSystem
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
        readonly EcsPoolInject<LocalComponent> _localPool = default;
        readonly EcsPoolInject<RemoteComponent> _remotePool = default;
        readonly EcsPoolInject<HealthComponent> _healthPool = default;
        readonly EcsPoolInject<AvatarViewComponent> _avatarViewPool = default;
        readonly EcsCustomInject<BoardView> _boardView = default;

        public void Init(IEcsSystems systems)
        {  
            var runner = PhotonInitializer.Instance?.Runner;
            if (runner == null)
            {
                Debug.LogError("[InitLocalPlayerSystem] NetworkRunner not found");
                return;
            }

            PlayerRef[] players = PhotonInitializer.Instance.ConnectedPlayers.ToArray();

            int localPlayerID = runner.LocalPlayer.PlayerId;

            // Детерминированно: меньший actorNumber = Side 1, больший = Side 2
            int minActorNumber = players.Min(p => p.PlayerId);

            bool isLocalPlayer = false;

            // Какие стороны реально заняты игроками — остальные аватары (напр. оппонент в соло) прячем.
            var sidePresent = new bool[3];   // индекс = сторона 1/2

            for (int i = 0; i < players.Length; i++)
            {
                int actorNumber = players[i].PlayerId; 

                isLocalPlayer = actorNumber == localPlayerID;

                int side = actorNumber == minActorNumber ? 1 : 2;
                if (side >= 1 && side <= 2) sidePresent[side] = true;

                int entity = _world.Value.NewEntity();

                ref var player = ref _playerPool.Value.Add(entity);
                player.PlayerId = actorNumber;
                player.IsLocalPlayer = isLocalPlayer;

                ref var playerSide = ref _sidePool.Value.Add(entity);
                playerSide.Side = side;

                ref var gold = ref _goldPool.Value.Add(entity);
                gold.Current = 0;
                gold.Max = 0;

                ref var mana = ref _manaPool.Value.Add(entity);
                mana.Current = 0;
                mana.Max = 0;

                ref var deck = ref _deckPool.Value.Add(entity);
                deck.CardEntities = new();
                deck.Count = 0;

                ref var hand = ref _handPool.Value.Add(entity);
                hand.CardEntities = new();
                hand.Count = 0;

                ref var healthComp = ref _healthPool.Value.Add(entity);
                healthComp.Current = 30;
                healthComp.Max = 30;

                if (_boardView.Value != null)
                {
                    var avatarView = _boardView.Value.GetAvatarView(side);
                    if (avatarView != null)
                    {
                        ref var avatarViewComp = ref _avatarViewPool.Value.Add(entity);
                        avatarViewComp.View = avatarView.gameObject;
                    }
                }

                if (isLocalPlayer)
                {
                    _state.Value.AddEntity(entity, Service.EntityService.PLAYER_ENTITY);
                    _localPool.Value.Add(entity);
                    // Префикс коротких NetworkEntityKey = id локального клиента (разводит ключи двух клиентов
                    // → ноль коллизий). Ставится до генерации колоды (InitDeckSystem идёт после).
                    Network.NetKey.SetClientPrefix(actorNumber);
                }
                else
                {
                    _state.Value.AddEntity(entity, Service.EntityService.OPPONENT_ENTITY);
                    _remotePool.Value.Add(entity);
                }

                GameEventBus.Publish(new PlayerAssignedEvent
                {
                    PlayerEntity = entity,
                    Side = side,
                    IsLocalPlayer = isLocalPlayer
                });

                Debug.Log($"[InitLocalPlayerSystem] Player entity={entity} playerId={actorNumber} side={side} isLocal={isLocalPlayer}");
            }

            // Прячем аватар + аватар-клетку для незанятых сторон (напр. оппонент в соло-режиме без 2-го игрока).
            if (_boardView.Value != null)
            {
                for (int side = 1; side <= 2; side++)
                {
                    if (sidePresent[side]) continue;
                    _boardView.Value.GetAvatarView(side)?.gameObject.SetActive(false);
                    _boardView.Value.GetAvatarCell(side)?.gameObject.SetActive(false);
                }
            }
        }

            }
        }
