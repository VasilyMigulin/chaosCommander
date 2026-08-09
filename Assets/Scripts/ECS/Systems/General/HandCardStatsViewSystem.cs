using System.Collections.Generic;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// Пушит живые статы карт-СУЩЕСТВ в руке ЛОКАЛЬНОГО игрока (OwnCardTag) в UI диффом:
    /// изменились Attack.Value / Health.Max / Health.Current (бафф в руке, урон командиру и т.п.) →
    /// HandCardStatsChangedUIEvent → PlayCardView красит статы относительно базы и панчит текст.
    /// Первое появление карты в кэше → Initial=true (первичная отрисовка без панча — карта могла
    /// прийти в руку уже баффнутой, напр. украденная со скидкой/бафом).
    /// Кост сюда НЕ входит — у него свой канал (CardAffordabilitySystem → CardCostChangedEvent).
    /// </summary>
    public sealed class HandCardStatsViewSystem : IEcsInitSystem, IEcsRunSystem
    {
        readonly EcsFilterInject<Inc<HandTag, CreatureTag, OwnCardTag, AttackComponent, HealthComponent>> _handCreatures = default;
        readonly EcsPoolInject<AttackComponent> _attackPool = default;
        readonly EcsPoolInject<HealthComponent> _hpPool = default;

        readonly EcsPoolInject<SpeedComponent> _speedPool = default;
        readonly Dictionary<int, (int atk, int max, int cur, int speed)> _last = new();
        readonly HashSet<int> _seenThisFrame = new();
        readonly List<int> _toPrune = new();
        readonly Queue<int> _viewPlaced = new();   // карты, чья вьюха только что создана (SetCard)

        public void Init(IEcsSystems systems)
            => GameEventBus.Subscribe<CardPlacedInHandViewEvent>(e => _viewPlaced.Enqueue(e.CardEntity));

        public void Run(IEcsSystems systems)
        {
            // Вьюха карты (пере)создана → сбросить кэш, чтобы следующий diff переиздал статы уже созданной вьюхе
            // (иначе Initial-событие ушло ДО создания PlayCardView — карта, пришедшая в руку изменённой:
            // tier-статы Королевской пиньяты / бафф-при-краже — оставалась на снэпшоте базовых значений).
            while (_viewPlaced.Count > 0) _last.Remove(_viewPlaced.Dequeue());

            _seenThisFrame.Clear();

            foreach (var e in _handCreatures.Value)
            {
                _seenThisFrame.Add(e);

                ref var atk = ref _attackPool.Value.Get(e);
                ref var hp  = ref _hpPool.Value.Get(e);
                int speed = _speedPool.Value.Has(e) ? _speedPool.Value.Get(e).Max : 0;
                var now = (atk.Value, hp.Max, hp.Current, speed);

                bool initial = !_last.TryGetValue(e, out var prev);
                if (!initial && prev == now) continue;

                _last[e] = now;
                GameEventBus.Publish(new HandCardStatsChangedUIEvent
                {
                    CardEntity    = e,
                    Attack        = atk.Value,
                    MaxHealth     = hp.Max,
                    CurrentHealth = hp.Current,
                    Speed         = speed,
                    Initial       = initial,
                });
            }

            // Карта ушла из руки → выкинуть из кэша: вернётся позже (баунс/командир после смерти) —
            // отрисуется заново как Initial, без ложного панча.
            _toPrune.Clear();
            foreach (var kv in _last)
                if (!_seenThisFrame.Contains(kv.Key)) _toPrune.Add(kv.Key);
            foreach (var e in _toPrune) _last.Remove(e);
        }
    }
}
