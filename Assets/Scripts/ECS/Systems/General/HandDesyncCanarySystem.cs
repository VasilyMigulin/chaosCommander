using Game.Core.Ecs.Components;
using Game.Core.Events;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// ДИАГНОСТИКА рассинхрона (временная). На старте каждого хода печатает руку и колоду каждого игрока
    /// по КЛЮЧАМ (NetworkEntityKey) на обоих клиентах. Diff «руки P2 у хоста (зеркало)» vs «реальной руки P2
    /// у клиента» показывает фантом / несовпадение ключей (напр. ключ сгенерированной карты разъехался).
    /// Убрать после отладки.
    /// </summary>
    public sealed class HandDesyncCanarySystem : IEcsInitSystem, IEcsDestroySystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsPoolInject<PlayerComponent>        _playerPool = default;
        readonly EcsPoolInject<HandComponent>          _handPool   = default;
        readonly EcsPoolInject<DeckComponent>          _deckPool   = default;
        readonly EcsPoolInject<NetworkEntityComponent> _netPool    = default;
        readonly EcsPoolInject<CardModelComponent>     _modelPool  = default;

        public void Init(IEcsSystems systems)    => GameEventBus.Subscribe<TurnStartedEvent>(OnTurnStart);
        public void Destroy(IEcsSystems systems) => GameEventBus.Unsubscribe<TurnStartedEvent>(OnTurnStart);

        void OnTurnStart(TurnStartedEvent e)
        {
            foreach (var pe in _world.Value.Filter<PlayerComponent>().Inc<HandComponent>().End())
            {
                ref var p = ref _playerPool.Value.Get(pe);
                ref var hand = ref _handPool.Value.Get(pe);
                int deckN = _deckPool.Value.Has(pe) ? _deckPool.Value.Get(pe).Count : 0;

                var sb = new System.Text.StringBuilder();
                sb.Append($"[Canary t{e.TurnNumber}] P{p.PlayerId} deck={deckN} hand[{hand.Count}]: ");
                if (hand.CardEntities != null)
                    foreach (var c in hand.CardEntities)
                    {
                        string key = _netPool.Value.Has(c)   ? _netPool.Value.Get(c).NetworkEntityKey : "noKey";
                        string nm  = _modelPool.Value.Has(c) ? _modelPool.Value.Get(c).CardName        : "?";
                        sb.Append($"{key}|{nm} ");
                    }
                UnityEngine.Debug.Log(sb.ToString());
            }
        }
    }
}
