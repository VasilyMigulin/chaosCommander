using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Mono;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Перехватывает управление когда на активном игроке висит PendingTargetCardComponent.
    ///
    /// Фрейм-1: подсвечивает все валидные цели на доске (CellHighlight.Target).
    /// Фрейм-N: обрабатывает CellClickEvent.
    ///   - Если кликнута валидная цель → создаёт CastEvent с TargetEntity / TargetCell,
    ///     снимает RequiresTargetSelectionTag, снимает PendingTargetCardComponent,
    ///     очищает подсветку.
    ///   - Если кликнута невалидная клетка → отменяет выбор карты (снимает Pending,
    ///     очищает подсветку). Карта остаётся в руке.
    /// </summary>
    public sealed class TargetSelectionSystem : IEcsRunSystem
    {
        readonly EcsCustomInject<BoardView> _boardView = default;

        // Активный игрок с pending-картой
        readonly EcsFilterInject<
            Inc<PendingTargetCardComponent, PlayerComponent, TurnState, TurnPhaseState>>
            _pendingFilter = default;
        readonly EcsPoolInject<PendingTargetCardComponent> _pendingPool = default;
        readonly EcsPoolInject<PlayerComponent>            _playerPool  = default;
        readonly EcsPoolInject<TurnPhaseState>             _phasePool   = default;

        // Клик
        readonly EcsFilterInject<Inc<CellClickEvent>> _clickFilter = default;
        readonly EcsPoolInject<CellClickEvent>        _clickPool   = default;

        // Карты
        readonly EcsPoolInject<RequiresTargetSelectionTag> _reqTargetPool  = default;
        readonly EcsPoolInject<CastEvent>                  _castPool        = default;
        readonly EcsPoolInject<OwnerComponent>             _ownerPool       = default;
        readonly EcsPoolInject<BoardPositionComponent>     _posPool         = default;

        // Существа на доске
        readonly EcsFilterInject<
            Inc<CreatureTag, BoardTag, BoardPositionComponent, OwnerComponent>,
            Exc<DeadTag>>
            _creaturesFilter = default;

        // Флаг что подсветка уже показана в этом pending-сеансе
        bool _highlightShown;
        int  _lastPendingCard = -1;

        public void Run(IEcsSystems systems)
        {
            // Проверяем: есть ли активный pending
            int pendingPlayerEntity = -1;
            foreach (var pe in _pendingFilter.Value)
            {
                ref var phase = ref _phasePool.Value.Get(pe);
                if (phase.Phase == TurnPhase.PlayerTurn)
                {
                    pendingPlayerEntity = pe;
                    break;
                }
            }

            if (pendingPlayerEntity < 0)
            {
                // Если pending сброшен — сбросим флаг подсветки
                _highlightShown = false;
                _lastPendingCard = -1;
                return;
            }

            ref var pending = ref _pendingPool.Value.Get(pendingPlayerEntity);
            ref var player  = ref _playerPool.Value.Get(pendingPlayerEntity);

            // Показываем подсветку один раз при смене pending-карты
            if (!_highlightShown || _lastPendingCard != pending.CardEntity)
            {
                ShowTargetHighlights(pending, player.PlayerId);
                _highlightShown  = true;
                _lastPendingCard = pending.CardEntity;
            }

            // Обрабатываем клик
            foreach (var clickEnt in _clickFilter.Value)
            {
                ref var click = ref _clickPool.Value.Get(clickEnt);

                // Только клики текущего игрока
                if (click.OwnerId != player.PlayerId)
                    continue;

                bool resolved = TryResolveTarget(pendingPlayerEntity, ref pending, ref player, ref click);

                if (!resolved)
                {
                    // Клик не по цели — отменяем выбор карты
                    CancelPending(pendingPlayerEntity, player.PlayerId);
                }
                break; // один клик за фрейм
            }
        }

        // ─── helpers ────────────────────────────────────────────────────────

        bool TryResolveTarget(
            int playerEntity,
            ref PendingTargetCardComponent pending,
            ref PlayerComponent player,
            ref CellClickEvent click)
        {
            int targetEntity = -1;
            int targetCell   = -1;

            switch (pending.RequiredTarget)
            {
                case TargetRequirementType.EnemyCreature:
                    targetEntity = FindCreatureAt(click.Row, click.Col, click.OwnerId,
                                                  player.PlayerId, isEnemy: true);
                    if (targetEntity < 0) return false;
                    break;

                case TargetRequirementType.AllyCreature:
                    targetEntity = FindCreatureAt(click.Row, click.Col, click.OwnerId,
                                                  player.PlayerId, isEnemy: false);
                    if (targetEntity < 0) return false;
                    break;

                case TargetRequirementType.AnyCreature:
                    targetEntity = FindCreatureAt(click.Row, click.Col, click.OwnerId,
                                                  player.PlayerId, isEnemy: false);
                    if (targetEntity < 0)
                        targetEntity = FindCreatureAt(click.Row, click.Col, click.OwnerId,
                                                      player.PlayerId, isEnemy: true);
                    if (targetEntity < 0) return false;
                    break;

                case TargetRequirementType.AnyCell:
                    targetCell = click.Row * 5 + click.Col;
                    break;
            }

            // Создаём CastEvent
            ref var castEvent = ref _castPool.Value.Add(pending.CardEntity);
            castEvent.OwnerEntity  = playerEntity;
            castEvent.TargetEntity = targetEntity;
            castEvent.TargetCell   = targetCell;

            // Снимаем тег «ожидает выбора цели»
            if (_reqTargetPool.Value.Has(pending.CardEntity))
                _reqTargetPool.Value.Del(pending.CardEntity);

            // Снимаем pending
            _pendingPool.Value.Del(playerEntity);
            _boardView.Value.ClearAllHighlights(player.PlayerId);
            _highlightShown  = false;
            _lastPendingCard = -1;

            return true;
        }

        void CancelPending(int playerEntity, int playerId)
        {
            ref var pending = ref _pendingPool.Value.Get(playerEntity);
            _pendingPool.Value.Del(playerEntity);
            _boardView.Value.ClearAllHighlights(playerId);
            _highlightShown  = false;
            _lastPendingCard = -1;

            GameEventBus.Publish(new TargetSelectionCancelledEvent { CardEntity = pending.CardEntity });
        }

        void ShowTargetHighlights(in PendingTargetCardComponent pending, int activePlayerId)
        {
            _boardView.Value.ClearAllHighlights(activePlayerId);

            foreach (var ce in _creaturesFilter.Value)
            {
                ref var owner = ref _ownerPool.Value.Get(ce);
                ref var pos   = ref _posPool.Value.Get(ce);

                bool isEnemy = owner.OwnerId != activePlayerId;

                bool shouldHighlight = pending.RequiredTarget switch
                {
                    TargetRequirementType.EnemyCreature => isEnemy,
                    TargetRequirementType.AllyCreature  => !isEnemy,
                    TargetRequirementType.AnyCreature   => true,
                    TargetRequirementType.AnyCell       => true,
                    _                                   => false,
                };

                if (!shouldHighlight) continue;

                var cell = _boardView.Value.GetCell(pos.Row, pos.Col, owner.OwnerId);
                cell?.SetHighlight(CellHighlight.Target);
            }
        }

        int FindCreatureAt(int row, int col, int clickOwnerId, int activePlayerId, bool isEnemy)
        {
            foreach (var ce in _creaturesFilter.Value)
            {
                ref var pos   = ref _posPool.Value.Get(ce);
                ref var owner = ref _ownerPool.Value.Get(ce);

                if (pos.Row != row || pos.Col != col || pos.OwnerId != clickOwnerId)
                    continue;

                bool creatureIsEnemy = owner.OwnerId != activePlayerId;
                if (creatureIsEnemy == isEnemy) return ce;
            }
            return -1;
        }
    }
}
