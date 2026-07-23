using System.Collections.Generic;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// БЛОК урона (Вуду-будду) — стоит ДО TakeDamageSystem. Игрок с ReflectDamageComponent на СВОЁМ ходу:
    /// снимаем TakeDamageEvent (урон не проходит, HP не трогается, без визуального скачка) и запоминаем
    /// величину в LastDamageTakenComponent — её возьмёт эффект-редирект чары-токена.
    ///   • ЛОКАЛЬНЫЙ владелец → ещё публикуем PlayerDamagedEvent → срабатывает способность чары (редирект
    ///     обычным пайплайном, синк через ActionAbilityData).
    ///   • УДАЛЁННЫЙ владелец (пассив) → только блок+величина, БЕЗ события: редирект приедет реплеем.
    ///
    /// «Свой ход» — по TurnStartedEvent (как MatchCounterTrackerSystem), НЕ по ActiveState. ActiveState
    /// вешается ТОЛЬКО после оседания стартового каскада (RunActivateSystem) и снимается в начале конечного,
    /// а урон от чар начала/конца хода и предсмертных хрипов приходит именно ВНЕ окна ActiveState. Со старым
    /// гейтом по ActiveState блок в этом окне не срабатывал: урон проходил по игроку, а редирект (по
    /// PlayerDamagedEvent из TakeDamageSystem) всё равно летел в оппонента → «и отразился, и по мне прошёл»
    /// → ничья вместо победы. Окно TurnStarted(владелец)→TurnStarted(другой) включает оба каскада.
    ///
    /// Урон на ЧУЖОМ ходу (бой) идёт через AttackHitEvent (не TakeDamageEvent) → сюда не попадает = не блок.
    /// </summary>
    public sealed class ReflectDamageSystem : IEcsRunSystem, IEcsInitSystem, IEcsDestroySystem, System.IDisposable
    {
        readonly EcsFilterInject<Inc<TakeDamageEvent, PlayerComponent, ReflectDamageComponent>> _filter = default;
        readonly EcsPoolInject<TakeDamageEvent> _dmgPool = default;
        readonly EcsPoolInject<LastDamageTakenComponent> _lastPool = default;
        readonly EcsPoolInject<LocalComponent> _localPool = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;

        // Чей сейчас ход (PlayerId) — зеркально на обоих клиентах (реплей публикует TurnStartedEvent тоже).
        int _currentTurnPlayerId = -1;
        bool _subscribed;

        public void Init(IEcsSystems systems)
        {
            if (_subscribed) return;
            _subscribed = true;
            GameEventBus.Subscribe<TurnStartedEvent>(OnTurnStarted);
        }

        // EcsSystems.Destroy() ищет IEcsDestroySystem, не System.IDisposable — без моста Dispose() не звался бы.
        public void Destroy(IEcsSystems systems) => Dispose();

        public void Dispose()
        {
            if (!_subscribed) return;
            GameEventBus.Unsubscribe<TurnStartedEvent>(OnTurnStarted);
            _subscribed = false;
        }

        void OnTurnStarted(TurnStartedEvent e) => _currentTurnPlayerId = e.ActivePlayerId;

        public void Run(IEcsSystems systems)
        {
            var buf = new List<int>();
            foreach (var e in _filter.Value) buf.Add(e);

            foreach (var e in buf)
            {
                if (!_dmgPool.Value.Has(e)) continue;

                // Блок только на ходу владельца (окно включает каскады начала/конца хода — там и бьют
                // чары/хрипы). Проверка зеркальна на обоих клиентах: _currentTurnPlayerId един.
                int ownerId = _playerPool.Value.Has(e) ? _playerPool.Value.Get(e).PlayerId : -1;
                if (ownerId < 0 || ownerId != _currentTurnPlayerId) continue;

                int amount = _dmgPool.Value.Get(e).Amount;
                if (!_lastPool.Value.Has(e)) _lastPool.Value.Add(e);
                _lastPool.Value.Get(e).Amount = amount;          // величина для эффекта-редиректа

                // Редирект гоним обычным пайплайном ТОЛЬКО у локального владельца (синк через
                // ActionAbilityData); у пассива блок есть, а редирект приедет реплеем.
                if (_localPool.Value.Has(e))
                    GameEventBus.Publish(new PlayerDamagedEvent { PlayerEntity = e, Amount = amount });

                _dmgPool.Value.Del(e);                           // БЛОК: урон не доходит до TakeDamageSystem
            }
        }
    }
}
