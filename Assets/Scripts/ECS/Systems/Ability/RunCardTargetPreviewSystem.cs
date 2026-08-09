using System.Collections.Generic;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Mono;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// Превью целей при удержании/отводе карты руки (синяя подсветка «готова к розыгрышу» —
    /// PlayCardView.StartRealDrag → CardDragTargetPreviewEvent, ДО фактического каста): подсвечивает на
    /// поле всех, кого заденет OnCast-способность карты — Field ВСЕМ подходящим, Target тем, кого МОЖНО
    /// назначить целью. Переиспользует CellHighlight.Target — ту же подсветку, что у интерактивного пика
    /// цели (RunAbilityTargetSelectionSystem), смысл тот же: «сюда попадёт».
    ///
    /// Правила способности (Rules — «SourceOnBoardRule» и т.п.) НЕ проверяются: карта ещё в руке
    /// (у существ — даже без клетки), большинство таких правил на этой стадии дали бы false и погасили
    /// превью battlecry-существ. Условия эффектов (ConditionRoot) тоже не проверяются — превью честно
    /// показывает КОГО ФИЛЬТРЫ СПОСОБНОСТИ СЧИТАЮТ ЦЕЛЬЮ, а не будет ли эффект применён.
    /// </summary>
    public sealed class RunCardTargetPreviewSystem : IEcsInitSystem, IEcsRunSystem, IEcsDestroySystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsCustomInject<BoardView> _boardView = default;

        readonly EcsPoolInject<AbilityContainerComponent> _containerPool = default;
        readonly EcsPoolInject<AbilityTriggerContainerComponent> _triggerPool = default;
        readonly EcsPoolInject<AbilityFieldComponent> _fieldPool = default;
        readonly EcsPoolInject<AbilityTargetComponent> _targetPool = default;
        readonly EcsPoolInject<AbilityOwnerComponent> _abilityOwnerPool = default;
        readonly EcsPoolInject<CreatureTag> _creatureTagPool = default;
        readonly EcsPoolInject<BoardPositionComponent> _posPool = default;
        readonly EcsPoolInject<PlayerSideComponent> _sidePool = default;

        readonly Queue<CardDragTargetPreviewEvent> _pending = new();
        readonly List<int> _highlightedCreatures = new();
        readonly List<int> _highlightedSides = new();

        public void Init(IEcsSystems systems) => GameEventBus.Subscribe<CardDragTargetPreviewEvent>(OnEvt);
        void OnEvt(CardDragTargetPreviewEvent e) => _pending.Enqueue(e);

        public void Run(IEcsSystems systems)
        {
            while (_pending.Count > 0)
            {
                var evt = _pending.Dequeue();
                Clear();
                if (evt.Active) Show(evt.CardEntity);
            }
        }

        void Show(int cardEntity)
        {
            if (_boardView.Value == null || !_containerPool.Value.Has(cardEntity)) return;
            var abilities = _containerPool.Value.Get(cardEntity).AbilityEntities;
            if (abilities == null) return;

            var creatureHits = new HashSet<int>();
            var sideHits = new HashSet<int>();

            foreach (var abilityEntity in abilities)
            {
                if (abilityEntity < 0 || !FiresOnCast(abilityEntity)) continue;
                if (!_abilityOwnerPool.Value.Has(abilityEntity)) continue;
                int playerEntity = _abilityOwnerPool.Value.Get(abilityEntity).PlayerEntity;

                List<int> candidates = null;
                if (_fieldPool.Value.Has(abilityEntity))
                {
                    ref var f = ref _fieldPool.Value.Get(abilityEntity);
                    candidates = TargetGather.Gather(_world.Value, f.Filters, cardEntity, playerEntity,
                                                     f.Area, f.Zone, f.IncludeCommanderInZones);
                }
                else if (_targetPool.Value.Has(abilityEntity))
                {
                    ref var t = ref _targetPool.Value.Get(abilityEntity);
                    candidates = TargetGather.Gather(_world.Value, t.Filters, cardEntity, playerEntity,
                                                     null, t.Zone, t.IncludeCommanderInZones);
                }
                if (candidates == null) continue;

                foreach (var c in candidates)
                {
                    if (_creatureTagPool.Value.Has(c)) creatureHits.Add(c);
                    else if (_sidePool.Value.Has(c)) sideHits.Add(_sidePool.Value.Get(c).Side);
                }
            }

            foreach (var ce in creatureHits)
            {
                if (!_posPool.Value.Has(ce)) continue;
                ref var p = ref _posPool.Value.Get(ce);
                _boardView.Value.GetCell(p.Row, p.Col, p.OwnerId)?.SetHighlight(CellHighlight.Target);
                _highlightedCreatures.Add(ce);
            }
            foreach (var side in sideHits)
            {
                _boardView.Value.GetAvatarCell(side)?.SetHighlight(CellHighlight.Target);
                _highlightedSides.Add(side);
            }
        }

        void Clear()
        {
            if (_boardView.Value != null)
            {
                foreach (var ce in _highlightedCreatures)
                {
                    if (!_posPool.Value.Has(ce)) continue;
                    ref var p = ref _posPool.Value.Get(ce);
                    _boardView.Value.GetCell(p.Row, p.Col, p.OwnerId)?.SetHighlight(CellHighlight.None);
                }
                foreach (var side in _highlightedSides)
                    _boardView.Value.GetAvatarCell(side)?.SetHighlight(CellHighlight.None);
            }
            _highlightedCreatures.Clear();
            _highlightedSides.Clear();
        }

        // OnCastTrigger.FiresOnCast=true (см. ITrigger) — только такие способности резолвятся ПРЯМО
        // на этом каске, остальные (OnDie/OnTurnStart/…) сработают позже по другому поводу.
        bool FiresOnCast(int abilityEntity)
        {
            if (!_triggerPool.Value.Has(abilityEntity)) return false;
            var triggers = _triggerPool.Value.Get(abilityEntity).Triggers;
            if (triggers == null) return false;
            foreach (var t in triggers)
                if (t != null && t.FiresOnCast) return true;
            return false;
        }

        public void Destroy(IEcsSystems systems)
        {
            GameEventBus.Unsubscribe<CardDragTargetPreviewEvent>(OnEvt);
            Clear();
        }
    }
}
