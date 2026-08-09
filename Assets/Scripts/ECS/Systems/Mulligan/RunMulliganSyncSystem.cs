using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Photon;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di; 

namespace Game.Core.Ecs.Systems 
{
    public sealed class RunMulliganSyncSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default; 
        readonly EcsPoolInject<OpponentDeckSyncEvent> _syncPool = default;
        readonly EcsCustomInject<PhotonRunHandler> _photon = default;
        readonly EcsFilterInject<Inc<PlayerComponent, DeckComponent>> _playerFilter = default; 

        readonly EcsFilterInject<Inc<PlayerComponent, RemoteComponent, DeckComponent, HandComponent, OpponentDeckSyncEvent>> _filter = default;

        public void Run (IEcsSystems systems) 
        {
            foreach (var entity in _filter.Value)
            {
                ref var sync = ref _syncPool.Value.Get(entity);
                ref var player = ref _playerPool.Value.Get(entity);

                for (int i = 0; i < sync.DeckCount; i++)
                {
                    GameEventBus.Publish(new CreateCardEvent
                    {
                        ExpansionId = sync.DeckExpansionIds[i],
                        CardId = sync.DeckCardIds[i],
                        NetworkEntityKey = sync.DeckNetworkKeys[i],
                        PlayerOwnerEntity = entity,   // сущность игрока-оппонента → способности с верным владельцем
                        OwnerId = player.PlayerId,
                        IsEnemy = true,
                        InHand = false,
                        // Регистрируем в списках зон оппонента → DeckComponent.Count верный и отслеживается при
                        // доборе/розыгрыше (MoveDeckCardToHand/RemoveFromHand). Без флага список пуст → счётчик
                        // руки/колоды оппонента на пассиве врал (баг 2). UI руки врага НЕ дублируется (IsEnemy-гейт).
                        RegisterInZoneList = true
                    });
                }

                for (int i = 0; i < sync.HandCount; i++)
                {
                    GameEventBus.Publish(new CreateCardEvent
                    {
                        ExpansionId = sync.HandExpansionIds[i],
                        CardId = sync.HandCardIds[i],
                        NetworkEntityKey = sync.HandNetworkKeys[i],
                        PlayerOwnerEntity = entity,
                        OwnerId = player.PlayerId,
                        IsEnemy = true,
                        InHand = true,
                        RegisterInZoneList = true   // см. коммент выше — счётчик руки оппонента
                    });
                }

                // Сайдборд оппонента: зеркало в своей зоне. RegisterInZoneList=false — у сайдборда нет
                // списка в компоненте игрока (в отличие от колоды/руки), зона задаётся только тегом.
                for (int i = 0; i < sync.SideboardCount; i++)
                {
                    GameEventBus.Publish(new CreateCardEvent
                    {
                        ExpansionId = sync.SideboardExpansionIds[i],
                        CardId = sync.SideboardCardIds[i],
                        NetworkEntityKey = sync.SideboardNetworkKeys[i],
                        PlayerOwnerEntity = entity,
                        OwnerId = player.PlayerId,
                        IsEnemy = true,
                        InSideboard = true,
                        RegisterInZoneList = false
                    });
                }

                GameEventBus.Publish(new CreateCardEvent()
                {
                    ExpansionId = sync.CommanderExpansionID,
                    CardId = sync.CommanderID,
                    NetworkEntityKey = sync.CommanderNetKey,
                    PlayerOwnerEntity = entity,
                    OwnerId = player.PlayerId,
                    IsEnemy = true,
                    InHand= true,
                    IsCommander = true,
                    RegisterInZoneList = true   // командир в руке на индексе 0 (как у локального в InitDeckSystem)
                });

                _syncPool.Value.Del(entity);
            }
        }
    }
}