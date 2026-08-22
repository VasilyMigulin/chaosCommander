using System.Collections.Generic;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Service;
using UnityEngine;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// ОЧЕРЕДЬ форс-розыгрышей (карты с AutoCastComponent: «при взятии разыграй» — Попойки/Вонючее облако,
    /// Фокус-покус, копии Подписки, выбор дискавера с PlayTargetCardEffect).
    ///
    /// БЫЛО (до 2026-07-31): все помеченные карты кастовались В ОДНОМ КАДРЕ, параллельно. Добор двух
    /// «Попоек» запускал два каста одновременно, поверх ещё идущей анимации раздачи руки — отсюда семья
    /// багов «рука полна, а карт меньше»: UI не успевал освободить слот (подавленное удаление + таймаут
    /// корутины), а карта оставалась в hand.CardEntities без HandTag (фантом). Обе половины лечились
    /// заплатками (SanitizeHand, эвристики таймаутов) — но причина была в ПАРАЛЛЕЛЬНОСТИ.
    ///
    /// СТАЛО: маркеры собираются в FIFO-очередь, розыгрыш идёт СТРОГО ПО ОДНОЙ карте:
    ///   • ждём, пока пайплайн осел (PipelineGate) — предыдущий каст полностью отработал;
    ///   • пейсинг ActionPacing.GapSeconds между кастами — каскад читаем игроку.
    ///
    /// НА UI НЕ ЗАВЯЗЫВАЕМСЯ. Здесь стояло ещё одно условие — «карта показана в руке»
    /// (CardPlacedInHandViewEvent) — со сторожевым таймаутом. При переполнении слотов руки вьюха не
    /// появлялась никогда, и ВЕСЬ каскад шёл по таймауту 4с на карту: в логе каждая карта «очередь ждала…
    /// кастую принудительно», между кастами очередь способностей успевала опуститься до нуля (из-за чего
    /// хрипы теряли связь с причиной), а часть способностей не резолвилась вовсе. Игровая очередь не
    /// должна ждать презентацию: UI — потребитель событий, а не гейт. Снято 2026-08-02.
    ///
    /// У ПАССИВА каст приезжает реплеем снапшотов — там очередь не нужна, маркер просто снимается.
    /// Регистрировать ДО RunCastRouterSystem (запрос обрабатывается в том же кадре).
    /// </summary>
    public sealed class AutoCastSystem : IEcsInitSystem, IEcsRunSystem, IEcsDestroySystem, System.IDisposable
    {
        readonly EcsWorldInject _world = default;
        readonly EcsPoolInject<AutoCastComponent> _autoPool = default;
        readonly EcsPoolInject<RequestCardCastEvent> _reqPool = default;
        readonly EcsPoolInject<CardModelComponent> _modelPool = default;
        readonly EcsFilterInject<Inc<AutoCastComponent>> _filter = default;

        struct Pending
        {
            public int Card;
            public bool Free;
        }

        readonly List<Pending> _queue = new();
        float _nextCastAt;

        public void Init(IEcsSystems systems) { }

        public void Destroy(IEcsSystems systems) => Dispose();

        public void Dispose() => _queue.Clear();

        public void Run(IEcsSystems systems)
        {
            var world = _world.Value;
            bool active = TurnGate.IsLocalActive(world);

            // 1) Новые маркеры → в конец очереди (FIFO: порядок появления = порядок розыгрыша).
            //    Маркер снимаем сразу на обоих клиентах — своё дело он сделал (пассив просто не кастует).
            foreach (var card in _filter.Value)
            {
                if (active)
                    _queue.Add(new Pending { Card = card, Free = _autoPool.Value.Get(card).Free });
                _autoPool.Value.Del(card);
            }

            if (_queue.Count == 0 || !active) return;

            // 2) Пейсинг между авто-кастами (читаемость каскада; тот же интервал, что у резолва/реплея).
            if (Time.time < _nextCastAt) return;

            var next = _queue[0];

            // Карта могла исчезнуть из мира (сброшена/уничтожена, пока ждала) — выкидываем из очереди.
            if (!_modelPool.Value.Has(next.Card))
            {
                _queue.RemoveAt(0);
                return;
            }

            // Предыдущий каст ещё крутится — ждём, чтобы касты не наслаивались. Это ЕДИНСТВЕННОЕ условие.
            if (!PipelineGate.IsSettled(world)) return;

            // УБРАНО ожидание вьюхи руки (2026-08-02). Раньше карта в руке не кастовалась, пока её вьюха
            // не ляжет в слот, а при переполнении слотов вьюха не появлялась НИКОГДА — и весь каскад шёл
            // по сторожевому таймауту 4с на карту. В логе это выглядело так: каждая карта каскада
            // «очередь ждала … дольше 4с», между кастами очередь способностей успевала опустеть, а
            // Облопошить и вовсе не отрезолвился. Игровая очередь не должна ждать презентацию: UI —
            // ПОТРЕБИТЕЛЬ событий, а не гейт. Слоты руки теперь сами становятся в очередь (CardLayout.
            // ReleaseSlot), так что подпирать их отсюда больше нечем.
            _queue.RemoveAt(0);

            if (!_reqPool.Value.Has(next.Card)) _reqPool.Value.Add(next.Card);
            ref var r = ref _reqPool.Value.Get(next.Card);
            r.CardEntity = next.Card;
            r.Free = next.Free;

            _nextCastAt = Time.time + ActionPacing.GapSeconds;
        }
    }
}
