using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Mono;
using System.Collections.Generic;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// Ввод локального активного игрока по клеткам: выбор существа (SelectTag), подсветка ВСЕХ
    /// достижимых клеток/целей в пределах оставшейся скорости (BFS по пустым клеткам — BoardNav) и
    /// превращение клика в МАРШРУТ (PathMoveComponent: несколько шагов + опциональная атака в конце) —
    /// «дойти куда угодно и ударить» одним кликом, вместо клика на каждый шаг.
    /// Исполняет маршрут RunPathMoveSystem (по одному MoveRequestEvent за оседание анимации);
    /// смежные цели без подхода — это просто маршрут с пустым списком шагов.
    /// </summary>
    public sealed class RunSelectCellSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsCustomInject<BoardView> _boardView = default;

        readonly EcsFilterInject<Inc<CellClickEvent>> _clickFilter = default;
        readonly EcsPoolInject<CellClickEvent> _clickPool = default;

        readonly EcsFilterInject<Inc<PlayerComponent, ActiveState>> _activePlayerFilter = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;

        // Поиск сущности игрока по стороне (для атаки по аватару).
        readonly EcsFilterInject<Inc<PlayerComponent, PlayerSideComponent>> _playersFilter = default;
        readonly EcsPoolInject<PlayerSideComponent> _sidePool = default;

        readonly EcsFilterInject<Inc<CreatureTag, BoardTag, BoardPositionComponent, SpeedComponent, OwnerComponent>, Exc<DeadTag>> _creaturesFilter = default;
        readonly EcsPoolInject<BoardPositionComponent> _posPool = default;
        readonly EcsPoolInject<SpeedComponent> _speedPool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;

        readonly EcsFilterInject<Inc<SelectTag>> _selectedFilter = default;
        readonly EcsPoolInject<SelectTag> _selectPool = default;

        readonly EcsPoolInject<PathMoveComponent> _pathPool = default;
        readonly EcsFilterInject<Inc<PathMoveComponent>> _pathFilter = default;
        readonly EcsPoolInject<AttacksUsedComponent> _attacksUsedPool = default;
        readonly EcsPoolInject<DoubleAttackTag> _doubleAttackPool = default;
        readonly EcsPoolInject<TauntTag> _tauntPool = default;
        readonly EcsPoolInject<StealthComponent> _stealthPool = default;

        // #4: сколько раз существо может атаковать за ход (сверх траты 1 скорости за атаку в AttackSystem).
        // База 1; свойство «Двойной удар» (DoubleAttackTag) поднимает лимит до 2.
        const int MaxAttacksPerTurn = 1;

        readonly EcsFilterInject<Inc<AttackAnimPendingTag>> _animPendingFilter = default;
        readonly EcsFilterInject<Inc<MovingTag>> _movingFilter = default;
        readonly EcsFilterInject<Inc<PendingOnCastComponent>> _pendingOnCastFilter = default;   // #2: призыв → OnCast

        readonly EcsFilterInject<Inc<PendingTargetCardComponent>> _pendingTargetFilter = default;
        readonly EcsFilterInject<Inc<PendingSelectCellState>> _pendingCellFilter = default;
        readonly EcsFilterInject<Inc<AbilityTargetPendingState>> _pendingAbilityTargetFilter = default;

        public void Run(IEcsSystems systems)
        {
            // Сброс выбора, переживающего конец хода: проверяем СОСТОЯНИЕ, а не событие (TurnEndedEvent
            // покрывал не все пути завершения — кнопка/таймер/MP; выбор и подсветка зависали на доске).
            // Локальный игрок не активен, а SelectTag есть → снять выбор и почистить подсветку.
            int activePlayerId = -1;
            foreach (var pe in _activePlayerFilter.Value)
            {
                if (!_playerPool.Value.Get(pe).IsLocalPlayer) continue;
                activePlayerId = _playerPool.Value.Get(pe).PlayerId;
                break;
            }

            if (activePlayerId < 0)
            {
                foreach (var se in _selectedFilter.Value) { Deselect(se); break; }
                return;
            }

            bool hasClick = _clickFilter.Value.GetEntitiesCount() > 0;

            if (_animPendingFilter.Value.GetEntitiesCount() > 0) { if (hasClick) UnityEngine.Debug.Log("[Select] blocked: AttackAnimPending"); return; }
            if (_movingFilter.Value.GetEntitiesCount() > 0)      { if (hasClick) UnityEngine.Debug.Log("[Select] blocked: Moving"); return; }
            // Исполняется маршрут (между шагами MovingTag на кадр пропадает) — клики не принимаем,
            // иначе можно выбрать/подвигать другое существо посреди чужого маршрута.
            if (_pathFilter.Value.GetEntitiesCount() > 0)        { if (hasClick) UnityEngine.Debug.Log("[Select] blocked: PathMove executing"); return; }
            if (_pendingOnCastFilter.Value.GetEntitiesCount() > 0) { if (hasClick) UnityEngine.Debug.Log("[Select] blocked: PendingOnCast (призыв→OnCast)"); return; }
            if (_pendingTargetFilter.Value.GetEntitiesCount() > 0) { if (hasClick) UnityEngine.Debug.Log("[Select] blocked: PendingTargetCard"); return; }
            if (_pendingCellFilter.Value.GetEntitiesCount() > 0) { if (hasClick) UnityEngine.Debug.Log("[Select] blocked: PendingSelectCell"); return; }             // размещение существа
            if (_pendingAbilityTargetFilter.Value.GetEntitiesCount() > 0) { if (hasClick) UnityEngine.Debug.Log("[Select] blocked: AbilityTargetPending"); return; }    // выбор цели способности

            foreach (var clickEntity in _clickFilter.Value)
            {
                ref var click   = ref _clickPool.Value.Get(clickEntity);
                int row         = click.Row;
                int col         = click.Col;
                int ownerId     = click.OwnerId;

                int selectedEntity = -1;
                foreach (var se in _selectedFilter.Value) { selectedEntity = se; break; }

                if (selectedEntity < 0)
                {
                    TrySelectCreature(row, col, ownerId, activePlayerId);
                }
                else
                {
                    ref var selPos   = ref _posPool.Value.Get(selectedEntity);
                    ref var selSpeed = ref _speedPool.Value.Get(selectedEntity);

                    if (selPos.Row == row && selPos.Col == col && selPos.OwnerId == ownerId)
                    {
                        Deselect(selectedEntity);
                        continue;
                    }

                    if (selSpeed.Remaining <= 0)
                    {
                        Deselect(selectedEntity);
                        continue;
                    }

                    // Достижимость: BFS по пустым клеткам в бюджете скорости (общая для клика и подсветки).
                    // «Защитник»: волна не идёт ДАЛЬШЕ клетки, смежной с вражеским Защитником — обойти/пройти
                    // мимо нельзя, только дойти вплотную и остановиться (см. BoardNav.ComputeReachable).
                    var reach = BoardNav.ComputeReachable(_creaturesFilter.Value, _posPool.Value,
                                                          selPos.Row, selPos.Col, selPos.OwnerId, selSpeed.Remaining,
                                                          cell => HasAdjacentEnemyTaunt(cell.row, cell.col, cell.owner, activePlayerId));

                    // Клик по аватар-клетке (-1,-1,side): «дойти до row 0 врага и ударить аватар» одним кликом.
                    // #5: бить аватар можно ТОЛЬКО с задней линии врага (row 0 его стороны). #4: 1 атака/ход.
                    // «Защитник»: если ПРЯМО СЕЙЧАС (без движения) рядом стоит вражеский Защитник — атаковать
                    // можно ТОЛЬКО его (др. цели, вкл. аватар, недоступны). Смотрим позицию ДО пути.
                    bool tauntBlocks = HasAdjacentEnemyTaunt(selPos.Row, selPos.Col, selPos.OwnerId, activePlayerId);

                    if (row == -1 && col == -1)
                    {
                        int avatarPlayer = FindPlayerBySide(ownerId);
                        bool isEnemyAvatar = avatarPlayer >= 0
                            && _playerPool.Value.Get(avatarPlayer).PlayerId != activePlayerId;
                        if (isEnemyAvatar && !tauntBlocks && CanAttack(selectedEntity)
                            && TryAvatarApproachPath(reach, ownerId, selSpeed.Remaining, out var toRow0))
                        {
                            StartPath(selectedEntity, reach.PathTo(toRow0), avatarPlayer);
                        }
                        Deselect(selectedEntity);
                        continue;
                    }

                    // «Скрытый»: вражеское существо со Stealth не выбирается кликом как цель атаки —
                    // FindCreatureAt(isEnemy:true) сам его пропускает.
                    int enemyEntity = FindCreatureAt(row, col, ownerId, activePlayerId, isEnemy: true);
                    if (enemyEntity >= 0)
                    {
                        bool tauntBlocksThis = tauntBlocks && !_tauntPool.Value.Has(enemyEntity);
                        // «Подойти и ударить»: ближайшая достижимая клетка, СМЕЖНАЯ с целью, + 1 скорость
                        // на саму атаку. Уже смежен → путь пустой (чистая атака, как раньше).
                        // #4: лимит атак; AttacksUsed спишет RunPathMoveSystem в момент удара.
                        if (!tauntBlocksThis && CanAttack(selectedEntity)
                            && TryApproachPath(reach, row, col, ownerId, selSpeed.Remaining, out var standCell))
                        {
                            StartPath(selectedEntity, reach.PathTo(standCell), enemyEntity);
                        }
                        Deselect(selectedEntity);
                        continue;
                    }

                    int allyOnCell = FindCreatureAt(row, col, ownerId, activePlayerId, isEnemy: false);
                    if (allyOnCell < 0)
                    {
                        // Пустая клетка: идём, если достижима в бюджете скорости (весь маршрут одним кликом).
                        if (reach.Cost.ContainsKey((row, col, ownerId)))
                            StartPath(selectedEntity, reach.PathTo((row, col, ownerId)), attackTarget: -1);
                        Deselect(selectedEntity);
                        continue;
                    }

                    Deselect(selectedEntity);
                    TrySelectCreature(row, col, ownerId, activePlayerId);
                }
            }

            void TrySelectCreature(int row, int col, int ownerId, int playerId)
            {
                int found = FindCreatureAt(row, col, ownerId, playerId, isEnemy: false);
                if (found < 0)
                {
                    UnityEngine.Debug.Log($"[Select] no OWN creature at ({row},{col},owner{ownerId}) for player {playerId}");
                    return;
                }

                ref var speed = ref _speedPool.Value.Get(found);
                if (speed.Remaining <= 0)
                {
                    UnityEngine.Debug.Log($"[Select] creature {found} not selectable: speed.Remaining=0 (Max={speed.Max})");
                    return;
                }

                _selectPool.Value.Add(found);
                GameEventBus.Publish(new CreatureSelectedEvent { CreatureEntity = found });
                HighlightOptions(found, playerId);
                UnityEngine.Debug.Log($"[Select] selected creature {found} at ({row},{col},owner{ownerId}) speed={speed.Remaining}");
            }

            // Маршрут одним кликом: шаги (может быть пусто — «ударить с места») + опциональная цель атаки.
            // Исполняет RunPathMoveSystem по одному шагу за оседание анимации; клики на время исполнения
            // блокируются гейтом PathMove выше.
            void StartPath(int creature, List<(int Row, int Col, int Owner)> steps, int attackTarget)
            {
                if (steps == null) return;
                if (steps.Count == 0 && attackTarget < 0) return;
                ref var path = ref _pathPool.Value.Add(creature);
                path.Steps = steps;
                path.AttackTargetEntity = attackTarget;
            }

            void Deselect(int entity)
            {
                if (!_selectPool.Value.Has(entity)) return;
                _selectPool.Value.Del(entity);
                GameEventBus.Publish(new CreatureDeselectedEvent());
                if (_boardView.Value != null)
                {
                    _boardView.Value.ClearAllHighlights(1);
                    _boardView.Value.ClearAllHighlights(2);
                    _boardView.Value.GetAvatarCell(1)?.SetHighlight(CellHighlight.None);
                    _boardView.Value.GetAvatarCell(2)?.SetHighlight(CellHighlight.None);
                }
            }

            int FindPlayerBySide(int side)
            {
                foreach (var pe in _playersFilter.Value)
                    if (_sidePool.Value.Get(pe).Side == side) return pe;
                return -1;
            }

            // #4: доступна ли ещё атака в этом ходу (лимит MaxAttacksPerTurn, +1 за «Двойной удар»).
            bool CanAttack(int e)
            {
                int used = _attacksUsedPool.Value.Has(e) ? _attacksUsedPool.Value.Get(e).Value : 0;
                int cap = MaxAttacksPerTurn + (_doubleAttackPool.Value.Has(e) ? 1 : 0);
                return used < cap;
            }

            // Ближайшая достижимая клетка, с которой бьётся цель (tr,tc,to): смежная с целью, стоимость
            // подхода + 1 (сама атака) укладывается в бюджет скорости. Стоим смежно → cost 0.
            bool TryApproachPath(BoardNav.Reachability reach, int tr, int tc, int to, int budget, out (int, int, int) standCell)
            {
                standCell = default;
                int best = int.MaxValue;
                foreach (var kv in reach.Cost)
                {
                    var (cr, cc, co) = kv.Key;
                    if (!BoardNav.IsNeighbour(cr, cc, co, tr, tc, to)) continue;
                    if (kv.Value + 1 > budget) continue;   // подход + удар
                    if (kv.Value < best) { best = kv.Value; standCell = kv.Key; }
                }
                return best != int.MaxValue;
            }

            // Подход для удара по аватару стороны side: ближайшая достижимая клетка row 0 этой стороны
            // (включая текущую позицию, если уже там), подход + 1 удар в бюджете.
            bool TryAvatarApproachPath(BoardNav.Reachability reach, int side, int budget, out (int, int, int) standCell)
            {
                standCell = default;
                int best = int.MaxValue;
                foreach (var kv in reach.Cost)
                {
                    var (cr, cc, co) = kv.Key;
                    if (cr != 0 || co != side) continue;
                    if (kv.Value + 1 > budget) continue;
                    if (kv.Value < best) { best = kv.Value; standCell = kv.Key; }
                }
                return best != int.MaxValue;
            }

            void HighlightOptions(int creatureEntity, int playerId)
            {
                if (_boardView.Value == null) return;

                _boardView.Value.ClearAllHighlights(1);
                _boardView.Value.ClearAllHighlights(2);
                _boardView.Value.GetAvatarCell(1)?.SetHighlight(CellHighlight.None);
                _boardView.Value.GetAvatarCell(2)?.SetHighlight(CellHighlight.None);

                ref var pos   = ref _posPool.Value.Get(creatureEntity);
                ref var speed = ref _speedPool.Value.Get(creatureEntity);

                _boardView.Value.GetCell(pos.Row, pos.Col, pos.OwnerId)?.SetHighlight(CellHighlight.Select);

                if (speed.Remaining <= 0) return;

                // Вся достижимость одним BFS: пустые клетки в бюджете → Move. «Защитник» тем же тупиком,
                // что и в клике (иначе подсветка обещала бы клетки, которые клик потом не даст занять).
                var reach = BoardNav.ComputeReachable(_creaturesFilter.Value, _posPool.Value,
                                                      pos.Row, pos.Col, pos.OwnerId, speed.Remaining,
                                                      cell => HasAdjacentEnemyTaunt(cell.row, cell.col, cell.owner, playerId));
                foreach (var kv in reach.Cost)
                {
                    if (kv.Key == reach.Start) continue;
                    var (cr, cc, co) = kv.Key;
                    _boardView.Value.GetCell(cr, cc, co)?.SetHighlight(CellHighlight.Move);
                }

                // #4: лимит атак исчерпан — атак-подсветок нет (двигаться ещё можно).
                if (!CanAttack(creatureEntity)) return;

                // «Защитник»: рядом (без движения) вражеский Защитник — подсвечиваем ТОЛЬКО его.
                bool tauntBlocks = HasAdjacentEnemyTaunt(pos.Row, pos.Col, pos.OwnerId, playerId);

                // Враги в радиусе «подойти + ударить» («Скрытые» не подсвечиваются — не выбираются кликом).
                foreach (var ce in _creaturesFilter.Value)
                {
                    ref var ep = ref _posPool.Value.Get(ce);
                    ref var eo = ref _ownerPool.Value.Get(ce);
                    if (eo.OwnerId == playerId) continue;
                    if (_stealthPool.Value.Has(ce)) continue;
                    if (tauntBlocks && !_tauntPool.Value.Has(ce)) continue;

                    if (TryApproachPath(reach, ep.Row, ep.Col, ep.OwnerId, speed.Remaining, out _))
                        _boardView.Value.GetCell(ep.Row, ep.Col, ep.OwnerId)?.SetHighlight(CellHighlight.Attack);
                }

                // #5: аватар врага — если достижима row 0 его стороны (или уже стоим там) с запасом на удар.
                if (tauntBlocks) return;   // Защитник рядом — аватар недоступен как цель
                foreach (var pe in _playersFilter.Value)
                {
                    if (_playerPool.Value.Get(pe).PlayerId == playerId) continue;
                    int side = _sidePool.Value.Get(pe).Side;
                    if (TryAvatarApproachPath(reach, side, speed.Remaining, out _))
                        _boardView.Value.GetAvatarCell(side)?.SetHighlight(CellHighlight.Attack);
                }
            }

            int FindCreatureAt(int row, int col, int ownerId, int playerId, bool isEnemy)
            {
                foreach (var ce in _creaturesFilter.Value)
                {
                    ref var p = ref _posPool.Value.Get(ce);
                    if (p.Row != row || p.Col != col || p.OwnerId != ownerId) continue;

                    ref var o = ref _ownerPool.Value.Get(ce);
                    if (isEnemy && o.OwnerId != playerId)
                    {
                        if (_stealthPool.Value.Has(ce)) continue;   // «Скрытый» враг не выбирается кликом
                        return ce;
                    }
                    if (!isEnemy && o.OwnerId == playerId) return ce;
                }
                return -1;
            }

            // «Защитник»: есть ли вражеский Защитник на клетке, СМЕЖНОЙ с (row,col,owner) — без движения.
            bool HasAdjacentEnemyTaunt(int row, int col, int owner, int playerId)
            {
                foreach (var (nr, nc, no) in BoardNav.GetNeighbours(row, col, owner))
                {
                    int ce = FindCreatureAt(nr, nc, no, playerId, isEnemy: true);
                    if (ce >= 0 && _tauntPool.Value.Has(ce)) return true;
                }
                return false;
            }
        }
    }
}
