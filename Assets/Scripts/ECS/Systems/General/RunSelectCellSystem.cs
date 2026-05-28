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

        readonly EcsFilterInject<Inc<PlayerComponent, TurnState, TurnPhaseState>> _activePlayerFilter = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsPoolInject<TurnPhaseState> _phasePool = default;

        readonly EcsFilterInject<Inc<CreatureTag, BoardTag, BoardPositionComponent, SpeedComponent, OwnerComponent>, Exc<DeadTag>> _creaturesFilter = default;
        readonly EcsPoolInject<BoardPositionComponent> _posPool = default;
        readonly EcsPoolInject<SpeedComponent> _speedPool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;

        readonly EcsFilterInject<Inc<SelectTag>> _selectedFilter = default;
        readonly EcsPoolInject<SelectTag> _selectPool = default;

        readonly EcsPoolInject<MoveRequestEvent> _movePool = default;
        readonly EcsPoolInject<AttackRequestEvent> _attackPool = default;

        readonly EcsFilterInject<Inc<AttackAnimPendingTag>> _animPendingFilter = default;
        readonly EcsFilterInject<Inc<MovingTag>> _movingFilter = default;

        readonly EcsFilterInject<Inc<PendingTargetCardComponent>> _pendingTargetFilter = default;

        public void Run(IEcsSystems systems)
        {
            if (_animPendingFilter.Value.GetEntitiesCount() > 0) return;
            if (_movingFilter.Value.GetEntitiesCount() > 0) return;
            if (_pendingTargetFilter.Value.GetEntitiesCount() > 0) return;

            int activePlayerId = -1;
            foreach (var pe in _activePlayerFilter.Value)
            {
                if (!_playerPool.Value.Get(pe).IsLocalPlayer) continue;
                ref var phase = ref _phasePool.Value.Get(pe);
                if (phase.Phase != TurnPhase.PlayerTurn) return;
                activePlayerId = _playerPool.Value.Get(pe).PlayerId;
                break;
            }
            if (activePlayerId < 0) return;

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

                    int enemyEntity = FindCreatureAt(row, col, ownerId, activePlayerId, isEnemy: true);
                    if (enemyEntity >= 0)
                    {
                        if (IsNeighbour(selPos.Row, selPos.Col, selPos.OwnerId, row, col, ownerId))
                        {
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
                if (found < 0) return;

                ref var speed = ref _speedPool.Value.Get(found);
                if (speed.Remaining <= 0) return;

                _selectPool.Value.Add(found);
                GameEventBus.Publish(new CreatureSelectedEvent { CreatureEntity = found });
                HighlightOptions(found, playerId);
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
                }
            }

            void HighlightOptions(int creatureEntity, int playerId)
            {
                if (_boardView.Value == null) return;

                _boardView.Value.ClearAllHighlights(1);
                _boardView.Value.ClearAllHighlights(2);

                ref var pos   = ref _posPool.Value.Get(creatureEntity);
                ref var speed = ref _speedPool.Value.Get(creatureEntity);

                _boardView.Value.GetCell(pos.Row, pos.Col, pos.OwnerId)?.SetHighlight(CellHighlight.Select);

                if (speed.Remaining <= 0) return;

                foreach (var (nr, nc, no) in GetNeighbours(pos.Row, pos.Col, pos.OwnerId))
                {
                    bool occupied = FindCreatureAt(nr, nc, no, playerId, isEnemy: false) >= 0
                                 || FindCreatureAt(nr, nc, no, playerId, isEnemy: true) >= 0;
                    if (!occupied)
                        _boardView.Value.GetCell(nr, nc, no)?.SetHighlight(CellHighlight.Move);
                }

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
