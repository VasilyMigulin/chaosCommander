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
    /// «Свой ход»: локальный — по ActiveState; удалённый — когда локальный НЕ активен (значит ход удалённого).
    /// Урон на ЧУЖОМ ходу (бой) идёт через AttackHitEvent (не TakeDamageEvent) → сюда не попадает = не блок.
    /// </summary>
    public sealed class ReflectDamageSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsFilterInject<Inc<TakeDamageEvent, PlayerComponent, ReflectDamageComponent>> _filter = default;
        readonly EcsPoolInject<TakeDamageEvent> _dmgPool = default;
        readonly EcsPoolInject<LastDamageTakenComponent> _lastPool = default;
        readonly EcsPoolInject<LocalComponent> _localPool = default;
        readonly EcsPoolInject<ActiveState> _activePool = default;

        public void Run(IEcsSystems systems)
        {
            bool localActive = TurnGate.IsLocalActive(_world.Value);

            var buf = new List<int>();
            foreach (var e in _filter.Value) buf.Add(e);

            foreach (var e in buf)
            {
                if (!_dmgPool.Value.Has(e)) continue;

                bool isLocalOwner = _localPool.Value.Has(e);
                bool ownerTurn = isLocalOwner ? _activePool.Value.Has(e) : !localActive;
                if (!ownerTurn) continue;                       // блок только на ходу владельца

                int amount = _dmgPool.Value.Get(e).Amount;
                if (!_lastPool.Value.Has(e)) _lastPool.Value.Add(e);
                _lastPool.Value.Get(e).Amount = amount;          // величина для эффекта-редиректа

                if (isLocalOwner)
                    GameEventBus.Publish(new PlayerDamagedEvent { PlayerEntity = e, Amount = amount });

                _dmgPool.Value.Del(e);                           // БЛОК: урон не доходит до TakeDamageSystem
            }
        }
    }
}
