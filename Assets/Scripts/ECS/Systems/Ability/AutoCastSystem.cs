using System.Collections.Generic;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// Форс-розыгрыш карт с AutoCastComponent (созданы авто-розыгрышем, напр. Фокус-покус: спеллы из пула).
    /// У АКТИВНОГО (IsLocalActive) ставит RequestCardCastEvent{Free} → карта идёт обычным каст-пайплайном
    /// (RunCastRouter → CardCastEvent → способность; синк через ActionCastData/ActionAbilityData). У ПАССИВА
    /// каст придёт реплеем тех же снапшотов, поэтому здесь только снимаем маркер (не кастуем повторно).
    /// ForceRandomTargetingComponent (персистентный) НЕ трогаем — таргетинг авто-выбирает цели (Йогг-Сарон).
    /// Регистрировать ДО RunCastRouterSystem (чтобы запрос обработался в том же кадре).
    /// </summary>
    public sealed class AutoCastSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsPoolInject<AutoCastComponent> _autoPool = default;
        readonly EcsPoolInject<RequestCardCastEvent> _reqPool = default;
        readonly EcsFilterInject<Inc<AutoCastComponent>> _filter = default;

        public void Run(IEcsSystems systems)
        {
            bool active = TurnGate.IsLocalActive(_world.Value);

            var list = new List<int>();
            foreach (var e in _filter.Value) list.Add(e);   // буфер: правим пул по ходу

            foreach (var card in list)
            {
                if (active)
                {
                    if (!_reqPool.Value.Has(card)) _reqPool.Value.Add(card);
                    ref var r = ref _reqPool.Value.Get(card);
                    r.CardEntity = card;
                    r.Free = true;
                }
                _autoPool.Value.Del(card);   // одноразовый триггер (снимаем на обоих)
            }
        }
    }
}
