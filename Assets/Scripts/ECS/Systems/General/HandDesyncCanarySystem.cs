using Game.Core.Ecs.Components;
using Game.Core.Events;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// ДИАГНОСТИКА рассинхрона (временная). На старте каждого хода печатает руку/колоду каждого игрока И
    /// СОСТОЯНИЕ ДОСКИ (существа по КЛЮЧАМ + статы + позиция) на обоих клиентах. Diff «руки/доски у хоста
    /// (зеркало)» vs «реальной у клиента» показывает фантом / рассинхрон (существо живо у одного, мертво у
    /// другого → BoardCanary покажет его в одном логе и не покажет в другом на СЛЕДУЮЩЕМ ходу). Убрать после отладки.
    /// </summary>
    public sealed class HandDesyncCanarySystem : IEcsInitSystem, IEcsDestroySystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsPoolInject<PlayerComponent>        _playerPool = default;
        readonly EcsPoolInject<HandComponent>          _handPool   = default;
        readonly EcsPoolInject<DeckComponent>          _deckPool   = default;
        readonly EcsPoolInject<NetworkEntityComponent> _netPool    = default;
        readonly EcsPoolInject<CardModelComponent>     _modelPool  = default;
        readonly EcsPoolInject<BoardPositionComponent> _posPool    = default;
        readonly EcsPoolInject<AttackComponent>        _atkPool    = default;
        readonly EcsPoolInject<HealthComponent>        _hpPool     = default;
        readonly EcsPoolInject<DeadTag>                _deadPool   = default;

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

            // Board canary: живые существа на доске (ключ + атака/HP + позиция/owner) — прямое сравнение доски
            // между клиентами. Существо, «умершее у одного, живое у другого», окажется в одном логе и отсутствует
            // в другом (или разное HP) — так виден ход и конкретное существо, где состояние разошлось.
            var bd = new System.Text.StringBuilder();
            bd.Append($"[BoardCanary t{e.TurnNumber}]: ");
            foreach (var ce in _world.Value.Filter<CreatureTag>().Inc<BoardTag>().Inc<BoardPositionComponent>().End())
            {
                string key = _netPool.Value.Has(ce)   ? _netPool.Value.Get(ce).NetworkEntityKey : "noKey";
                string nm  = _modelPool.Value.Has(ce) ? _modelPool.Value.Get(ce).CardName        : "?";
                ref var pos = ref _posPool.Value.Get(ce);
                int atk = _atkPool.Value.Has(ce) ? _atkPool.Value.Get(ce).Value   : 0;
                int hpC = _hpPool.Value.Has(ce)  ? _hpPool.Value.Get(ce).Current  : 0;
                int hpM = _hpPool.Value.Has(ce)  ? _hpPool.Value.Get(ce).Max      : 0;
                string dead = _deadPool.Value.Has(ce) ? "[DEAD]" : "";
                bd.Append($"{key}|{nm} {atk}/{hpC}({hpM}) @({pos.Row},{pos.Col},o{pos.OwnerId}){dead} ");
            }
            UnityEngine.Debug.Log(bd.ToString());
        }
    }
}
