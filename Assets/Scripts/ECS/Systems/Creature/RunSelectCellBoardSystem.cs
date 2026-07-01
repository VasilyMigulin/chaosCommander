using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Mono;
using UnityEngine;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// Размещение существа. Пока карта в PendingSelectCellState — подсвечивает свободные клетки
    /// передней линии (row 0) своей стороны и ждёт CellClickEvent (отдельная event-сущность от
    /// BattleState.OnCellSelected). Валидный клик → MoveCardToBoardEvent + InvokeEvent на карте,
    /// снимает pending и подсветку.
    /// </summary>
    public sealed class RunSelectCellBoardSystem : IEcsRunSystem
    {
        const int FrontRow = 0;

        readonly EcsCustomInject<BoardView> _boardView = default;
        readonly EcsWorldInject _world = default;

        readonly EcsFilterInject<Inc<PendingSelectCellState>> _pendingFilter = default;
        readonly EcsPoolInject<PendingSelectCellState> _pendingPool = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsPoolInject<DeclineCardCastEvent> _declinePool = default;

        readonly EcsFilterInject<Inc<CellClickEvent>> _clickFilter = default;
        readonly EcsPoolInject<CellClickEvent> _clickPool = default;

        readonly EcsPoolInject<MoveCardToBoardEvent> _movePool = default;
        readonly EcsPoolInject<InvokeEvent> _invokePool = default;

        readonly EcsPoolInject<BoardPositionComponent> _posPool = default;
        readonly EcsFilterInject<Inc<CreatureTag, BoardTag, BoardPositionComponent>> _boardCreatures = default;

        int _highlightedFor = -1;

        public void Run(IEcsSystems systems)
        {
            // первая карта, ожидающая размещения
            int card = -1;
            foreach (var e in _pendingFilter.Value) { card = e; break; }

            if (card < 0)
            {
                if (_highlightedFor >= 0) { _boardView.Value?.ClearAllHighlights(); _highlightedFor = -1; }
                return;
            }

            ref var pending = ref _pendingPool.Value.Get(card);
            int ownerPlayerEntity = pending.OwnerPlayerEntity;
            int ownerPlayerId = _playerPool.Value.Get(ownerPlayerEntity).PlayerId;

            // подсветка свободных клеток фронта — один раз на карту
            if (_highlightedFor != card)
            {
                Highlight(ownerPlayerId);
                _highlightedFor = card;
            }

            // ПКМ — отмена (удобство в редакторе; на телефоне ПКМ нет — там отменяет тап мимо клетки).
            if (Input.GetMouseButtonDown(1))
            {
                Cancel(card, ownerPlayerEntity);
                return;
            }

            // клики приходят отдельными event-сущностями (BattleState.OnCellSelected)
            foreach (var clickEntity in _clickFilter.Value)
            {
                ref var click = ref _clickPool.Value.Get(clickEntity);

                bool valid = click.Row == FrontRow && click.OwnerId == ownerPlayerId &&
                             !IsOccupied(click.Row, click.Col, click.OwnerId);

                if (!valid)
                {
                    Cancel(card, ownerPlayerEntity);   // тап мимо валидной клетки = отмена (мобайл)
                    break;
                }

                if (!_movePool.Value.Has(card))
                {
                    ref var m = ref _movePool.Value.Add(card);
                    m.Row = click.Row; m.Col = click.Col; m.OwnerId = click.OwnerId;
                }
                if (!_invokePool.Value.Has(card)) _invokePool.Value.Add(card);

                _pendingPool.Value.Del(card);
                _boardView.Value?.ClearAllHighlights();
                _highlightedFor = -1;
                break;   // один клик за кадр
            }
        }

        void Cancel(int card, int playerEntity)
        {
            CastCost.Refund(_world.Value, card, playerEntity);
            if (!_declinePool.Value.Has(card)) _declinePool.Value.Add(card).Reason = DeclineReason.Unknown;
            GameEventBus.Publish(new TargetSelectionCancelledEvent { CardEntity = card });   // UI вернёт карту в руку
            _pendingPool.Value.Del(card);
            _boardView.Value?.ClearAllHighlights();
            _highlightedFor = -1;
        }

        void Highlight(int ownerPlayerId)
        {
            if (_boardView.Value == null) return;
            _boardView.Value.ClearAllHighlights();
            for (int col = 0; col < 5; col++)
            {
                if (IsOccupied(FrontRow, col, ownerPlayerId)) continue;
                _boardView.Value.GetCell(FrontRow, col, ownerPlayerId)?.SetHighlight(CellHighlight.Target);
            }
        }

        bool IsOccupied(int row, int col, int ownerId)
        {
            foreach (var ce in _boardCreatures.Value)
            {
                ref var p = ref _posPool.Value.Get(ce);
                if (p.Row == row && p.Col == col && p.OwnerId == ownerId) return true;
            }
            return false;
        }
    }
}
