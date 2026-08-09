using System.Collections.Generic;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Mono;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// Подсветка клеток, СВОИМИ существами на которых ещё можно походить в этом ходу (CellView.SetReady —
    /// слой 2, независимый от ситуативной подсветки хода/атаки/цели).
    ///
    /// ЗАЧЕМ: показатель скорости на вью неочевиден, и по доске не читается, кем ты уже походил, а кем нет
    /// (жалоба с плейтеста 2026-07-31). Критерий — РОВНО тот же, по которому существо вообще выбирается
    /// кликом (RunSelectCellSystem.TrySelectCreature: speed.Remaining > 0), иначе подсветка обещала бы ход
    /// там, где ввод его не даст.
    ///
    /// ДИФФ, А НЕ ПОКАДРОВОЕ ПРИМЕНЕНИЕ: набор «готовых» клеток пересобирается каждый тик (≤10 существ на
    /// сторону — копейки), но вью трогаются ТОЛЬКО на изменении набора. Событийную подписку тут сознательно
    /// не делаем: Remaining меняют движение, атака, старт хода, баффы, ауры, чары, смена уровня и полиморф —
    /// единого сигнала нет, и любая подписка со временем начнёт пропускать источник (класс багов «подсветка
    /// отстала от статов»). Сравнение набора верно по построению при ЛЮБОЙ причине изменения.
    /// </summary>
    public sealed class CellReadyHighlightSystem : IEcsRunSystem
    {
        readonly EcsCustomInject<BoardView> _boardView = default;

        readonly EcsFilterInject<Inc<PlayerComponent, ActiveState>> _activePlayerFilter = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;

        readonly EcsFilterInject<Inc<CreatureTag, BoardTag, BoardPositionComponent, SpeedComponent, OwnerComponent>, Exc<DeadTag>> _creatures = default;
        readonly EcsPoolInject<BoardPositionComponent> _posPool = default;
        readonly EcsPoolInject<SpeedComponent> _speedPool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;

        // Пока игрок выбирает цель (розыгрыш карты / способность / размещение существа) доска и так
        // расписана оранжевым — фоновая подсветка «можно ходить» в этот момент только мешает читать выбор.
        readonly EcsFilterInject<Inc<PendingTargetCardComponent>> _pendingTargetFilter = default;
        readonly EcsFilterInject<Inc<PendingSelectCellState>> _pendingCellFilter = default;
        readonly EcsFilterInject<Inc<AbilityTargetPendingState>> _pendingAbilityTargetFilter = default;

        readonly HashSet<(int Row, int Col, int Owner)> _cur = new();
        readonly HashSet<(int Row, int Col, int Owner)> _prev = new();

        public void Run(IEcsSystems systems)
        {
            var board = _boardView.Value;
            if (board == null) return;

            _cur.Clear();
            Collect();

            if (_cur.SetEquals(_prev)) return;   // ничего не поменялось — вью не трогаем вовсе

            foreach (var cell in _prev)
                if (!_cur.Contains(cell))
                    board.GetCell(cell.Row, cell.Col, cell.Owner)?.SetReady(false);

            foreach (var cell in _cur)
                if (!_prev.Contains(cell))
                    board.GetCell(cell.Row, cell.Col, cell.Owner)?.SetReady(true);

            _prev.Clear();
            foreach (var cell in _cur) _prev.Add(cell);
        }

        void Collect()
        {
            // Не наш ход (или идёт выбор цели) → набор пуст, дифф сам погасит всё, что горело.
            int localActiveId = -1;
            foreach (var pe in _activePlayerFilter.Value)
            {
                ref var p = ref _playerPool.Value.Get(pe);
                if (!p.IsLocalPlayer) continue;
                localActiveId = p.PlayerId;
                break;
            }
            if (localActiveId < 0) return;

            if (_pendingTargetFilter.Value.GetEntitiesCount() > 0) return;
            if (_pendingCellFilter.Value.GetEntitiesCount() > 0) return;
            if (_pendingAbilityTargetFilter.Value.GetEntitiesCount() > 0) return;

            foreach (var ce in _creatures.Value)
            {
                if (_ownerPool.Value.Get(ce).OwnerId != localActiveId) continue;
                if (_speedPool.Value.Get(ce).Remaining <= 0) continue;

                ref var pos = ref _posPool.Value.Get(ce);
                _cur.Add((pos.Row, pos.Col, pos.OwnerId));
            }
        }
    }
}
