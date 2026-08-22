using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Instance.Card;
using System.Collections.Generic;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// Удержание на клетке (CellView → CreatureHoldUIEvent) → найти существо в клетке, собрать
    /// CardVisualData из его CardViewDataComponent (CardVisualDataFactory.From) и попросить UI показать
    /// карточку-инспектор (CardDetailUIEvent). Show=false просто форвардит закрытие без поиска.
    /// Удержание на АВАТАР-клетке (Row=-1, Col=-1, OwnerId=сторона) показывает карту КОМАНДИРА этого
    /// игрока — в какой бы зоне командир ни был (рука/колода/борд/кулдаун после смерти).
    /// Работает для любой стороны — это просмотр информации, не выбор хода.
    /// </summary>
    public sealed class CreatureInspectSystem : IEcsInitSystem, IEcsRunSystem
    {
        readonly EcsPoolInject<BoardPositionComponent> _posPool = default;
        readonly EcsPoolInject<CardViewDataComponent> _viewDataPool = default;
        readonly EcsFilterInject<Inc<CreatureTag, BoardTag, BoardPositionComponent>, Exc<DeadTag>> _creaturesFilter = default;

        // Аватар-инспект: командир стороны (по владельцу; зона не важна — карта командира одна).
        readonly EcsFilterInject<Inc<CommanderTag, OwnerComponent, CardViewDataComponent>> _commandersFilter = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;
        readonly EcsFilterInject<Inc<PlayerComponent, PlayerSideComponent>> _playersFilter = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsPoolInject<PlayerSideComponent> _sidePool = default;

        readonly Queue<CreatureHoldUIEvent> _pending = new Queue<CreatureHoldUIEvent>();

        // Не отписываемся (как RunDiscoverSystem и др.) — живёт до GameEventBus.Clear() в EcsRunHandler.Dispose.
        public void Init(IEcsSystems systems) => GameEventBus.Subscribe<CreatureHoldUIEvent>(e => _pending.Enqueue(e));

        public void Run(IEcsSystems systems)
        {
            while (_pending.Count > 0) Handle(_pending.Dequeue());
        }

        void Handle(CreatureHoldUIEvent e)
        {
            if (!e.Show)
            {
                GameEventBus.Publish(new CardDetailUIEvent { Show = false });
                return;
            }

            // Аватар-клетка: показать карту командира стороны e.OwnerId.
            // СВОЙ аватар — попап не нужен (он ложится поверх aura/charm-бара над своим аватаром и не
            // даёт нажать на миниатюры аур: просьба юзера 2026-08-21). Чужой — оставляем, это осмысленный
            // просмотр вражеского командира.
            if (e.Row == -1 && e.Col == -1)
            {
                if (IsLocalSide(e.OwnerId)) return;

                int commander = FindCommanderBySide(e.OwnerId);
                if (commander < 0) return;
                GameEventBus.Publish(new CardDetailUIEvent
                {
                    Visual = CardVisualDataFactory.From(in _viewDataPool.Value.Get(commander)),
                    Show   = true,
                });
                return;
            }

            int found = -1;
            foreach (var ce in _creaturesFilter.Value)
            {
                ref var p = ref _posPool.Value.Get(ce);
                if (p.Row == e.Row && p.Col == e.Col && p.OwnerId == e.OwnerId) { found = ce; break; }
            }
            if (found < 0) return;

            var visual = _viewDataPool.Value.Has(found)
                ? CardVisualDataFactory.From(in _viewDataPool.Value.Get(found))
                : default;

            GameEventBus.Publish(new CardDetailUIEvent { Visual = visual, Show = true });
        }

        // Сторона side принадлежит ЛОКАЛЬНОМУ игроку (для гейта «не показывать попап над своим аватаром»).
        bool IsLocalSide(int side)
        {
            foreach (var pe in _playersFilter.Value)
                if (_sidePool.Value.Get(pe).Side == side) return _playerPool.Value.Get(pe).IsLocalPlayer;
            return false;
        }

        // Командир игрока, чья сторона доски = side (аватар-клетка несёт сторону, не playerId).
        int FindCommanderBySide(int side)
        {
            int playerId = -1;
            foreach (var pe in _playersFilter.Value)
                if (_sidePool.Value.Get(pe).Side == side) { playerId = _playerPool.Value.Get(pe).PlayerId; break; }
            if (playerId < 0) return -1;

            foreach (var ce in _commandersFilter.Value)
                if (_ownerPool.Value.Get(ce).OwnerId == playerId) return ce;
            return -1;
        }
    }
}
