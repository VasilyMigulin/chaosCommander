using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Mono;
using UnityEngine;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Обрабатывает клики по клеткам поля (CellSelectedEvent).
    ///
    /// Логика:
    ///   1. Нет выбранного существа → кликнули на своё существо → выбираем,
    ///      подсвечиваем доступные ходы / атаки.
    ///   2. Есть выбранное существо → кликнули на подсвеченную клетку:
    ///      - своя пустая клетка → MoveRequestEvent (если SpeedComponent.Remaining > 0)
    ///      - клетка с врагом    → AttackRequestEvent (если SpeedComponent.Remaining > 0)
    ///   3. Кликнули куда-то ещё → снимаем выбор.
    /// Не работает если фаза не PlayerTurn или висит AttackAnimPendingTag.
    /// </summary>
    public sealed class RunSelectCellSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsCustomInject<BoardView> _boardView = default;

        // Ивент клика
        readonly EcsFilterInject<Inc<CellClickEvent>> _clickFilter = default;
        readonly EcsPoolInject<CellClickEvent> _clickPool = default;

        // Игрок
        readonly EcsFilterInject<Inc<PlayerComponent, TurnState, TurnPhaseState>> _activePlayerFilter = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsPoolInject<TurnPhaseState> _phasePool = default;

        // Существа на доске
        readonly EcsFilterInject<
            Inc<CreatureTag, BoardTag, BoardPositionComponent, SpeedComponent, OwnerComponent>,
            Exc<DeadTag>> _creaturesFilter = default;
        readonly EcsPoolInject<BoardPositionComponent> _posPool = default;
        readonly EcsPoolInject<SpeedComponent> _speedPool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;

        // Выбор
        readonly EcsFilterInject<Inc<SelectTag>> _selectedFilter = default;
        readonly EcsPoolInject<SelectTag> _selectPool = default;

        // Запросы
        readonly EcsPoolInject<MoveRequestEvent> _movePool = default;
        readonly EcsPoolInject<AttackRequestEvent> _attackPool = default;

        // Блокировка анимации
        readonly EcsFilterInject<Inc<AttackAnimPendingTag>> _animPendingFilter = default;

        // Блокировка выбора существ пока ждём выбора цели карты
        readonly EcsFilterInject<Inc<PendingTargetCardComponent>> _pendingTargetFilter = default;

        // Вью
        readonly EcsPoolInject<ViewRefComponent> _viewPool = default;


        public void Run(IEcsSystems systems)
        {
            // Не обрабатываем клики пока идёт анимация
            if (_animPendingFilter.Value.GetEntitiesCount() > 0) return;

            // Уступаем управление TargetSelectionSystem пока игрок выбирает цель карты
            if (_pendingTargetFilter.Value.GetEntitiesCount() > 0) return;

            // Только в фазе хода игрока
            int activePlayerId = -1;
            foreach (var pe in _activePlayerFilter.Value)
            {
                ref var phase = ref _phasePool.Value.Get(pe);
                if (phase.Phase != TurnPhase.PlayerTurn) return;
                activePlayerId = _playerPool.Value.Get(pe).PlayerId;
            }
            if (activePlayerId < 0) return;

            foreach (var clickEntity in _clickFilter.Value)
            {
                ref var click = ref _clickPool.Value.Get(clickEntity);
                int row = click.Row;
                int col = click.Col;
                int ownerId = click.OwnerId;

                // Нашли ли уже выбранное существо
                int selectedEntity = -1;
                foreach (var se in _selectedFilter.Value)
                {
                    selectedEntity = se;
                    break;
                }

                if (selectedEntity < 0)
                {
                    // Попытка выбрать своё существо на кликнутой клетке
                    TrySelectCreature(row, col, ownerId, activePlayerId);
                }
                else
                {
                    ref var selPos   = ref _posPool.Value.Get(selectedEntity);
                    ref var selSpeed = ref _speedPool.Value.Get(selectedEntity);

                    // Проверяем: ткнули ли на того же существа → отмена
                    if (selPos.Row == row && selPos.Col == col && selPos.OwnerId == ownerId)
                    {
                        Deselect(selectedEntity);
                        continue;
                    }

                    if (selSpeed.Remaining <= 0)
                    {
                        // Нет зарядов — просто снимаем выбор
                        Deselect(selectedEntity);
                        continue;
                    }

                    // Проверяем: есть ли враг на клетке
                    int enemyEntity = FindCreatureAt(row, col, ownerId, activePlayerId, isEnemy: true);
                    if (enemyEntity >= 0)
                    {
                        // Атака
                        ref var attackReq = ref _attackPool.Value.Add(selectedEntity);
                        attackReq.TargetEntity = enemyEntity;
                        Deselect(selectedEntity);
                        continue;
                    }

                    // Проверяем: пустая своя клетка → ход
                    int allyOnCell = FindCreatureAt(row, col, ownerId, activePlayerId, isEnemy: false);
                    if (allyOnCell < 0 && ownerId == activePlayerId)
                    {
                        ref var moveReq = ref _movePool.Value.Add(selectedEntity);
                        moveReq.ToRow = row;
                        moveReq.ToCol = col;
                        Deselect(selectedEntity);
                        continue;
                    }

                    // Иначе — снимаем выбор и пробуем выбрать другое
                    Deselect(selectedEntity);
                    TrySelectCreature(row, col, ownerId, activePlayerId);
                }
            }
        }

        void TrySelectCreature(int row, int col, int ownerId, int activePlayerId)
        {
            if (ownerId != activePlayerId) return;

            int found = FindCreatureAt(row, col, ownerId, activePlayerId, isEnemy: false);
            if (found < 0) return;

            ref var speed = ref _speedPool.Value.Get(found);
            if (speed.Remaining <= 0) return;

            _selectPool.Value.Add(found);
            GameEventBus.Publish(new CreatureSelectedEvent { CreatureEntity = found });

            HighlightOptions(found, activePlayerId);
        }

        void Deselect(int entity)
        {
            _selectPool.Value.Del(entity);
            GameEventBus.Publish(new CreatureDeselectedEvent());

            if (_boardView.Value != null)
            {
                // Убираем подсветку для обеих сторон поля владельца
                ref var owner = ref _ownerPool.Value.Get(entity);
                _boardView.Value.ClearAllHighlights(owner.OwnerId);
            }
        }

        void HighlightOptions(int creatureEntity, int activePlayerId)
        {
            if (_boardView.Value == null) return;

            _boardView.Value.ClearAllHighlights(activePlayerId);

            ref var pos = ref _posPool.Value.Get(creatureEntity);
            var selfCell = _boardView.Value.GetCell(pos.Row, pos.Col, pos.OwnerId);
            selfCell?.SetHighlight(CellHighlight.Select);

            // Подсвечиваем соседние свои пустые клетки (ход)
            for (int dr = -1; dr <= 1; dr++)
            {
                for (int dc = -1; dc <= 1; dc++)
                {
                    if (dr == 0 && dc == 0) continue;
                    int nr = pos.Row + dr;
                    int nc = pos.Col + dc;
                    if (nr < 0 || nc < 0) continue;

                    bool occupied = FindCreatureAt(nr, nc, activePlayerId, activePlayerId, isEnemy: false) >= 0
                                 || FindCreatureAt(nr, nc, activePlayerId, activePlayerId, isEnemy: true) >= 0;
                    if (!occupied)
                    {
                        var cell = _boardView.Value.GetCell(nr, nc, activePlayerId);
                        cell?.SetHighlight(CellHighlight.Move);
                    }
                }
            }

            // Подсвечиваем врагов которых можно атаковать (соседние по своей+чужой стороне)
            foreach (var ce in _creaturesFilter.Value)
            {
                ref var ep = ref _posPool.Value.Get(ce);
                ref var eo = ref _ownerPool.Value.Get(ce);
                if (eo.OwnerId == activePlayerId) continue;

                // Простая доступность: любой враг в пределах 1 хода (с учётом зеркальности)
                // Здесь намеренно оставлено как "любой враг" — расширить при необходимости
                var enemyCell = _boardView.Value.GetCell(ep.Row, ep.Col, eo.OwnerId);
                enemyCell?.SetHighlight(CellHighlight.Attack);
            }
        }

        /// <summary>Ищет существо на клетке. isEnemy=true ищет не принадлежащих activePlayerId.</summary>
        int FindCreatureAt(int row, int col, int ownerId, int activePlayerId, bool isEnemy)
        {
            foreach (var ce in _creaturesFilter.Value)
            {
                ref var p = ref _posPool.Value.Get(ce);
                ref var o = ref _ownerPool.Value.Get(ce);
                if (p.Row != row || p.Col != col) continue;

                bool isOwner = o.OwnerId == activePlayerId;
                if (isEnemy && !isOwner && ownerId != activePlayerId) return ce;
                if (!isEnemy && isOwner && o.OwnerId == ownerId) return ce;
            }
            return -1;
        }
    }
}
