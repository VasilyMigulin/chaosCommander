using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Mono;
using System.Collections.Generic;

namespace Game.Core.Ecs.Systems
{
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

        readonly EcsPoolInject<MoveRequestEvent> _movePool = default;
        readonly EcsPoolInject<AttackRequestEvent> _attackPool = default;
        readonly EcsPoolInject<AttacksUsedComponent> _attacksUsedPool = default;

        // #4: сколько раз существо может атаковать за ход (сверх траты 1 скорости за атаку в AttackSystem).
        // База 1; бонусы («Неистовство ветра» и т.п.) добавятся отдельным компонентом-модификатором позже.
        const int MaxAttacksPerTurn = 1;

        readonly EcsFilterInject<Inc<AttackAnimPendingTag>> _animPendingFilter = default;
        readonly EcsFilterInject<Inc<MovingTag>> _movingFilter = default;
        readonly EcsFilterInject<Inc<PendingOnCastComponent>> _pendingOnCastFilter = default;   // #2: призыв → OnCast

        readonly EcsFilterInject<Inc<PendingTargetCardComponent>> _pendingTargetFilter = default;
        readonly EcsFilterInject<Inc<PendingSelectCellState>> _pendingCellFilter = default;
        readonly EcsFilterInject<Inc<AbilityTargetPendingState>> _pendingAbilityTargetFilter = default;

        public void Run(IEcsSystems systems)
        {
            bool hasClick = _clickFilter.Value.GetEntitiesCount() > 0;

            if (_animPendingFilter.Value.GetEntitiesCount() > 0) { if (hasClick) UnityEngine.Debug.Log("[Select] blocked: AttackAnimPending"); return; }
            if (_movingFilter.Value.GetEntitiesCount() > 0)      { if (hasClick) UnityEngine.Debug.Log("[Select] blocked: Moving"); return; }
            if (_pendingOnCastFilter.Value.GetEntitiesCount() > 0) { if (hasClick) UnityEngine.Debug.Log("[Select] blocked: PendingOnCast (призыв→OnCast)"); return; }
            if (_pendingTargetFilter.Value.GetEntitiesCount() > 0) { if (hasClick) UnityEngine.Debug.Log("[Select] blocked: PendingTargetCard"); return; }
            if (_pendingCellFilter.Value.GetEntitiesCount() > 0) { if (hasClick) UnityEngine.Debug.Log("[Select] blocked: PendingSelectCell"); return; }             // размещение существа
            if (_pendingAbilityTargetFilter.Value.GetEntitiesCount() > 0) { if (hasClick) UnityEngine.Debug.Log("[Select] blocked: AbilityTargetPending"); return; }    // выбор цели способности

            int activePlayerId = -1;
            foreach (var pe in _activePlayerFilter.Value)
            {
                if (!_playerPool.Value.Get(pe).IsLocalPlayer) continue;
                activePlayerId = _playerPool.Value.Get(pe).PlayerId;
                break;
            }
            if (activePlayerId < 0) { if (hasClick) UnityEngine.Debug.Log("[Select] blocked: local player not active (no ActiveState)"); return; }

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

                    // Клик по аватар-клетке (-1,-1,side): атака по аватару вражеского игрока.
                    // #5: бить аватар можно ТОЛЬКО с задней линии врага (row 0 его стороны) — ближайшей
                    // к аватару, а не с любой клетки его половины. #4: не более 1 атаки за ход.
                    if (row == -1 && col == -1)
                    {
                        int avatarPlayer = FindPlayerBySide(ownerId);
                        bool isEnemyAvatar = avatarPlayer >= 0
                            && _playerPool.Value.Get(avatarPlayer).PlayerId != activePlayerId;
                        if (isEnemyAvatar && selSpeed.Remaining > 0 && selPos.OwnerId == ownerId
                            && selPos.Row == 0 && CanAttack(selectedEntity))
                        {
                            MarkAttacked(selectedEntity);
                            ref var attackReq = ref _attackPool.Value.Add(selectedEntity);
                            attackReq.TargetEntity = avatarPlayer;
                        }
                        Deselect(selectedEntity);
                        continue;
                    }

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

                    int enemyEntity = FindCreatureAt(row, col, ownerId, activePlayerId, isEnemy: true);
                    if (enemyEntity >= 0)
                    {
                        // #4: атака 1 раз за ход (сверх траты 1 скорости в AttackSystem).
                        if (IsNeighbour(selPos.Row, selPos.Col, selPos.OwnerId, row, col, ownerId)
                            && CanAttack(selectedEntity))
                        {
                            MarkAttacked(selectedEntity);
                            ref var attackReq = ref _attackPool.Value.Add(selectedEntity);
                            attackReq.TargetEntity = enemyEntity;
                        }
                        Deselect(selectedEntity);
                        continue;
                    }

                    int allyOnCell = FindCreatureAt(row, col, ownerId, activePlayerId, isEnemy: false);
                    if (allyOnCell < 0)
                    {
                        if (IsNeighbour(selPos.Row, selPos.Col, selPos.OwnerId, row, col, ownerId))
                        {
                            ref var moveReq   = ref _movePool.Value.Add(selectedEntity);
                            moveReq.ToRow     = row;
                            moveReq.ToCol     = col;
                            moveReq.ToOwnerId = ownerId;
                        }
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

            // #4: доступна ли ещё атака в этом ходу (лимит MaxAttacksPerTurn).
            bool CanAttack(int e)
            {
                int used = _attacksUsedPool.Value.Has(e) ? _attacksUsedPool.Value.Get(e).Value : 0;
                return used < MaxAttacksPerTurn;
            }

            void MarkAttacked(int e)
            {
                if (!_attacksUsedPool.Value.Has(e)) _attacksUsedPool.Value.Add(e);
                _attacksUsedPool.Value.Get(e).Value++;
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

                // #4: если лимит атак за ход исчерпан — атак-подсветки не показываем (двигаться ещё можно).
                bool canAttack = CanAttack(creatureEntity);

                // #5: аватар врага атакуем ТОЛЬКО с задней линии врага (row 0 его стороны) — подсветить
                // аватар-клетку как цель, если существо там стоит и атака ещё доступна.
                int standSide = pos.OwnerId;
                int standSidePlayer = FindPlayerBySide(standSide);
                if (canAttack && pos.Row == 0 && standSidePlayer >= 0
                    && _playerPool.Value.Get(standSidePlayer).PlayerId != playerId)
                    _boardView.Value.GetAvatarCell(standSide)?.SetHighlight(CellHighlight.Attack);

                foreach (var (nr, nc, no) in GetNeighbours(pos.Row, pos.Col, pos.OwnerId))
                {
                    bool occupied = FindCreatureAt(nr, nc, no, playerId, isEnemy: false) >= 0
                                 || FindCreatureAt(nr, nc, no, playerId, isEnemy: true) >= 0;
                    if (!occupied)
                        _boardView.Value.GetCell(nr, nc, no)?.SetHighlight(CellHighlight.Move);
                }

                if (canAttack)
                    foreach (var ce in _creaturesFilter.Value)
                    {
                        ref var ep = ref _posPool.Value.Get(ce);
                        ref var eo = ref _ownerPool.Value.Get(ce);
                        if (eo.OwnerId == playerId) continue;

                        if (IsNeighbour(pos.Row, pos.Col, pos.OwnerId, ep.Row, ep.Col, eo.OwnerId))
                            _boardView.Value.GetCell(ep.Row, ep.Col, ep.OwnerId)?.SetHighlight(CellHighlight.Attack);
                    }
            }

            IEnumerable<(int row, int col, int owner)> GetNeighbours(int row, int col, int owner)
            {
                if (col > 0) yield return (row, col - 1, owner);
                if (col < 4) yield return (row, col + 1, owner);
                // Назад (в тыл своей половины)
                if (row > 0) yield return (row - 1, col, owner);
                // Вперёд: row=0→row=1 (внутри своей стороны), row=1→row=1 другого owner (пересечение фронта)
                if (row < 1)
                    yield return (row + 1, col, owner);
                else
                    yield return (1, col, owner == 1 ? 2 : 1);
            }

            bool IsNeighbour(int r1, int c1, int o1, int r2, int c2, int o2)
            {
                foreach (var (nr, nc, no) in GetNeighbours(r1, c1, o1))
                    if (nr == r2 && nc == c2 && no == o2) return true;
                return false;
            }

            int FindCreatureAt(int row, int col, int ownerId, int playerId, bool isEnemy)
            {
                foreach (var ce in _creaturesFilter.Value)
                {
                    ref var p = ref _posPool.Value.Get(ce);
                    if (p.Row != row || p.Col != col || p.OwnerId != ownerId) continue;

                    ref var o = ref _ownerPool.Value.Get(ce);
                    if (isEnemy  && o.OwnerId != playerId) return ce;
                    if (!isEnemy && o.OwnerId == playerId) return ce;
                }
                return -1;
            }
        }
    }
}
