using System.Collections.Generic;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using UnityEngine;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// Брокер ЕДИНСТВЕННОГО окна выбора карт (PickupWindow). Окно одно, а желающих несколько:
    /// RunDiscoverSystem (раскопка), RunDrawReplacementSystem (Адовый червь), RunAbilityPickSelectionSystem
    /// (цель из колоды/руки/кладбища), CardPickSelectionSystem (пик перед кастом, легаси). Без арбитра они
    /// публиковали CardPickOfferedEvent наперегонки в одном кадре, и последний перебивал предыдущего:
    /// окно Развилки мигало и подменялось окном Адового червя, а запрос Развилки оставался жить без UI.
    ///
    /// Две обязанности, обе — единственные в проекте:
    ///
    /// 1. ВЫДАЧА СЛОТА. Продюсер вешает PickTicketComponent (см. PickTicket.Ready) и ждёт Granted.
    ///    Брокер держит выданным ровно один талон, следующий выдаёт по наименьшему RequestId — то есть
    ///    в порядке появления запросов (FIFO). Талон живёт на сущности продюсера, поэтому смерть запроса
    ///    освобождает окно сама; head-of-line блокировки «мёртвым» держателем не существует.
    ///
    /// 2. ИСТЕЧЕНИЕ. Правило «пик не переживает конец хода» раньше было размазано: раскопка форсила
    ///    случайный выбор, выбор цели отменялся, а замена добора и пик перед кастом не обрабатывались
    ///    ВООБЩЕ (зависший PendingDrawReplacementComponent глушил добор до конца матча). Теперь момент
    ///    задаёт брокер один раз — CardPickExpiredEvent, — а КАК сворачивать решает каждый продюсер сам
    ///    (раскопка/червь — случайный выбор, таргетинг/каст — отмена). Событие адресовано по PlayerId,
    ///    поэтому продюсер гасит и те свои запросы, что талона ещё не брали.
    ///
    /// СТРАХОВКА: талон, не снятый продюсером за кадр после истечения, брокер снимает сам и пишет
    /// предупреждение — окно физически не может остаться занятым навсегда.
    ///
    /// СИНК: система чисто презентационная (см. PickTicketComponent) — на сеть не влияет.
    /// Ставить в пайплайн ДО всех продюсеров пика.
    /// </summary>
    public sealed class CardPickBrokerSystem : IEcsInitSystem, IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;

        readonly EcsFilterInject<Inc<PickTicketComponent>> _ticketFilter = default;
        readonly EcsPoolInject<PickTicketComponent>        _ticketPool   = default;
        readonly EcsPoolInject<PlayerComponent>            _playerPool   = default;

        readonly Queue<int> _turnEndedOwners = new Queue<int>();
        readonly List<int>  _buffer          = new List<int>();

        public void Init(IEcsSystems systems)
        {
            PickRequestId.Reset();   // мир пересоздаётся на матч — токены нужны уникальными в его пределах
            GameEventBus.Subscribe<TurnEndedEvent>(e => _turnEndedOwners.Enqueue(e.ActivePlayerId));
        }

        public void Run(IEcsSystems systems)
        {
            CollectAbandoned();
            while (_turnEndedOwners.Count > 0) Expire(_turnEndedOwners.Dequeue());
            Grant();
        }

        // Талон помечен истёкшим, но продюсер не снял его за прошедший кадр — снимаем сами, иначе окно
        // осталось бы занятым до конца матча. Это баг продюсера, поэтому громко.
        void CollectAbandoned()
        {
            _buffer.Clear();
            foreach (var e in _ticketFilter.Value)
                if (_ticketPool.Value.Get(e).Expired) _buffer.Add(e);

            foreach (var e in _buffer)
            {
                if (!_ticketPool.Value.Has(e)) continue;
                Debug.LogWarning($"[PickBroker] талон #{_ticketPool.Value.Get(e).RequestId} не снят продюсером после истечения хода — освобождаю окно принудительно");
                _ticketPool.Value.Del(e);
            }
        }

        // Ход игрока закончился: все его пики должны свернуться. Момент — здесь, способ — у продюсера.
        void Expire(int playerId)
        {
            foreach (var e in _ticketFilter.Value)
            {
                ref var t = ref _ticketPool.Value.Get(e);
                if (PlayerIdOf(t.PlayerEntity) != playerId) continue;
                t.Expired = true;
            }

            // Адресуем по игроку, а не по талону: у продюсера могут быть запросы, ещё не бравшие слот
            // (например вторая раскопка одного каста, ждущая своей очереди) — их тоже надо свернуть.
            GameEventBus.Publish(new CardPickExpiredEvent { PlayerId = playerId });
        }

        // Ровно один выданный талон. Следующий — с наименьшим RequestId (порядок появления запросов).
        void Grant()
        {
            int head = -1, headId = int.MaxValue;

            foreach (var e in _ticketFilter.Value)
            {
                ref var t = ref _ticketPool.Value.Get(e);
                if (t.Granted) return;   // окно занято живым пиком — ждём его резолва
                if (t.Expired) continue; // истёкшие слот не получают, их снимет CollectAbandoned
                if (t.RequestId < headId) { headId = t.RequestId; head = e; }
            }

            if (head >= 0) _ticketPool.Value.Get(head).Granted = true;
        }

        int PlayerIdOf(int playerEntity)
            => (playerEntity >= 0 && _playerPool.Value.Has(playerEntity))
                ? _playerPool.Value.Get(playerEntity).PlayerId
                : -1;
    }
}
