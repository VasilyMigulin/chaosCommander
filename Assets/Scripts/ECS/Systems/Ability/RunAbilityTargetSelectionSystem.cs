using System;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Mono;
using Game.Core.Shared.Interface;
using UnityEngine;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// Async-выбор цели игроком для Target/Selected. Берёт ability-сущность с AbilityTargetPendingState
    /// (чей ход активен), подсвечивает валидные цели (AbilityTargetComponent.Filters), ловит CellClickEvent:
    ///   валидная цель → копит Chosen; набрав Count → AbilityQueuedState{Targets};
    ///   невалидный/правый клик → отмена (способность не разыгрывается).
    /// UI-механику (BoardView highlight, CellClickEvent) переиспользуем из старого target-select.
    /// </summary>
    public sealed class RunAbilityTargetSelectionSystem : IEcsRunSystem
    {
        readonly EcsCustomInject<BoardView> _boardView = default;
        readonly EcsWorldInject _world = default;

        readonly EcsFilterInject<Inc<AbilityTargetPendingState, AbilityTargetComponent, AbilityOwnerComponent>> _pendingFilter = default;
        readonly EcsPoolInject<AbilityTargetPendingState> _pendingPool = default;
        readonly EcsPoolInject<AbilityTargetComponent>    _targetPool  = default;
        readonly EcsPoolInject<AbilityOwnerComponent>     _ownerPool   = default;
        readonly EcsPoolInject<AbilityQueuedState>        _queuedPool  = default;

        readonly EcsFilterInject<Inc<CellClickEvent>> _clickFilter = default;
        readonly EcsPoolInject<CellClickEvent>        _clickPool   = default;

        readonly EcsPoolInject<PlayerComponent>   _playerPool = default;
        readonly EcsPoolInject<ActiveState>       _activePool = default;

        readonly EcsFilterInject<Inc<CreatureTag, BoardTag, BoardPositionComponent, OwnerComponent>, Exc<DeadTag>> _creaturesFilter = default;
        readonly EcsPoolInject<BoardPositionComponent> _posPool = default;

        // Игроки (аватары) как ВОЗМОЖНЫЕ цели (напр. OpponentTargetFilter — «Поджог» и любой спелл по врагу
        // ИЛИ вражескому игроку): без этого Selected-таргетинг мог только целить существ, клик по аватару
        // (row=-1,col=-1 — см. RunSelectCellSystem) не находил кандидата → способность фуззлилась впустую
        // (ресурсы уже списаны роутером). Side, не PlayerId — тот же паттерн, что у атаки аватара.
        readonly EcsFilterInject<Inc<PlayerComponent, PlayerSideComponent>> _playersFilter = default;
        readonly EcsPoolInject<PlayerSideComponent> _sidePool = default;

        bool _highlightShown;
        int  _lastPending = -1;

        public void Run(IEcsSystems systems)
        {
            int ability = -1, playerEntity = -1;
            foreach (var e in _pendingFilter.Value)
            {
                int pe = _pendingPool.Value.Get(e).PlayerEntity;
                if (IsActiveTurn(pe)) { ability = e; playerEntity = pe; break; }
            }

            if (ability < 0) { if (_highlightShown) ClearHighlight(); return; }   // пендинг ушёл → снять подсветку/хинт

            ref var owner = ref _ownerPool.Value.Get(ability);
            ref var tc    = ref _targetPool.Value.Get(ability);

            if (!_highlightShown || _lastPending != ability)
            {
                ShowHighlights(owner.CardEntity, owner.PlayerEntity, tc.Filters);
                GameEventBus.Publish(new TargetSelectionHintEvent { Show = true, Text = "Выберите цель" });
                _highlightShown = true;
                _lastPending = ability;
            }

            if (Input.GetMouseButtonDown(1)) { Cancel(ability); return; }

            foreach (var clickEnt in _clickFilter.Value)
            {
                ref var click = ref _clickPool.Value.Get(clickEnt);
                int candidate = FindCandidate(in click, owner.CardEntity, owner.PlayerEntity, tc.Filters);
                if (candidate >= 0) Choose(ability, candidate, tc.Count);
                else Cancel(ability);
                break;   // один клик за кадр
            }
        }

        bool IsActiveTurn(int playerEntity) => _activePool.Value.Has(playerEntity);

        void Choose(int ability, int candidate, int count)
        {
            ref var pending = ref _pendingPool.Value.Get(ability);
            var chosen = pending.Chosen ?? Array.Empty<int>();
            foreach (var c in chosen) if (c == candidate) return;   // не дублируем цель

            var next = new int[chosen.Length + 1];
            Array.Copy(chosen, next, chosen.Length);
            next[chosen.Length] = candidate;
            pending.Chosen = next;

            if (next.Length >= count)
            {
                if (!_queuedPool.Value.Has(ability)) _queuedPool.Value.Add(ability);
                _queuedPool.Value.Get(ability).Targets = next;
                _pendingPool.Value.Del(ability);
                ClearHighlight();
            }
        }

        void Cancel(int ability)
        {
            if (_pendingPool.Value.Has(ability)) _pendingPool.Value.Del(ability);
            ClearHighlight();
            // нет AbilityQueuedState → способность не разыграется. TODO: вернуть ресурсы каста при необходимости.
        }

        void ClearHighlight()
        {
            _boardView.Value.ClearAllHighlights();
            GameEventBus.Publish(new TargetSelectionHintEvent { Show = false });   // снять хинт «выберите цель»
            _highlightShown = false;
            _lastPending = -1;
        }

        void ShowHighlights(int casterCard, int casterPlayer, ITargetFilter[] filters)
        {
            _boardView.Value.ClearAllHighlights();
            foreach (var ce in _creaturesFilter.Value)
            {
                if (!TargetGather.FiltersOk(_world.Value, ce, casterCard, casterPlayer, filters)) continue;
                ref var pos = ref _posPool.Value.Get(ce);
                // ВАЖНО: клетка ищется по BoardPositionComponent.OwnerId («на чьей половине физически стоит
                // существо» — меняется при пересечении середины поля), а НЕ по OwnerComponent.OwnerId
                // («кто контролирует» — не меняется при пересечении). Раньше здесь читался OwnerComponent
                // (_creOwnerPool) → для существа, перешедшего на чужую половину, подсвечивалась ЧУЖАЯ клетка
                // (его старая сторона), а не та, где оно реально стоит («Поджог» и любой Selected/Board таргетинг).
                _boardView.Value.GetCell(pos.Row, pos.Col, pos.OwnerId)?.SetHighlight(CellHighlight.Target);
            }

            // Игроки (аватары) как кандидаты (OpponentTargetFilter и т.п.) — подсвечиваем клетку аватара.
            foreach (var pe in _playersFilter.Value)
            {
                if (!TargetGather.FiltersOk(_world.Value, pe, casterCard, casterPlayer, filters)) continue;
                int side = _sidePool.Value.Get(pe).Side;
                _boardView.Value.GetAvatarCell(side)?.SetHighlight(CellHighlight.Target);
            }
        }

        int FindCandidate(in CellClickEvent click, int casterCard, int casterPlayer, ITargetFilter[] filters)
        {
            // Клик по аватару (row=-1,col=-1 — конвенция RunSelectCellSystem): цель — сущность ИГРОКА
            // соответствующей СТОРОНЫ (Side, не PlayerId — см. комментарий у _playersFilter).
            if (click.Row == -1 && click.Col == -1)
            {
                foreach (var pe in _playersFilter.Value)
                {
                    if (_sidePool.Value.Get(pe).Side != click.OwnerId) continue;
                    return TargetGather.FiltersOk(_world.Value, pe, casterCard, casterPlayer, filters) ? pe : -1;
                }
                return -1;
            }

            foreach (var ce in _creaturesFilter.Value)
            {
                ref var pos = ref _posPool.Value.Get(ce);
                // Сверка клика — ТОЖЕ по BoardPositionComponent.OwnerId (см. комментарий в ShowHighlights).
                if (pos.Row != click.Row || pos.Col != click.Col || pos.OwnerId != click.OwnerId) continue;
                if (!TargetGather.FiltersOk(_world.Value, ce, casterCard, casterPlayer, filters)) continue;
                return ce;
            }
            return -1;
        }
    }
}
