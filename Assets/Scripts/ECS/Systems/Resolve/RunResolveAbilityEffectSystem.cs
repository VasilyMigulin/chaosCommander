using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Service;
using Game.Core.Shared.Interface;
using System.Collections.Generic;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Создаёт effect-entity для текущего шага цепочки способности.
    ///
    /// Гейтится тегом NeedsStepResolveTag — его ставит AbilityChainAdvanceSystem
    /// при входе и после завершения каждого шага.
    ///
    /// Шаг 0: использует основные Effects/Target/Mode/Shape абилки (как раньше).
    /// Шаг N > 0: использует ChainSteps[N-1].Effects и цели по ChainTargetSource.
    ///
    /// Захват клетки (CapturedRow/Col/OwnerId) делается на шаге 0 от первой цели,
    /// если та стоит на доске — потом эффект MoveSourceToCell использует её как
    /// «займи место уничтоженного».
    ///
    /// Снэпшот AbilityResolvedNetEvent публикуется per-step (свои карты) — пассивный
    /// клиент применит каждый шаг отдельно.
    /// </summary>
    public sealed class RunResolveAbilityEffectSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsSharedInject<IGameStateContext> _state = default;

        readonly EcsFilterInject<
            Inc<ResolveAbilityEvent, NeedsStepResolveTag, ChainStateComponent>,
            Exc<ConditionNotMetTag>> _filter = default;

        readonly EcsPoolInject<ResolveAbilityEvent> _resolvePool = default;
        readonly EcsPoolInject<AbilityEffectContainerComponent> _effectContainerPool = default;
        readonly EcsPoolInject<AbilityChainContainerComponent> _chainContainerPool = default;
        readonly EcsPoolInject<ChainStateComponent> _chainStatePool = default;
        readonly EcsPoolInject<NeedsStepResolveTag> _needsStepResolvePool = default;
        readonly EcsPoolInject<AbilityChosenTargetComponent> _chosenTargetPool = default;
        readonly EcsPoolInject<TargetMaskComponent> _targetFlagsPool = default;
        readonly EcsPoolInject<ProjectileViewComponent> _projectileViewPool = default;

        readonly EcsPoolInject<EffectComponent> _effectPool = default;
        readonly EcsPoolInject<TargetEntityComponent> _targetEntityPool = default;
        readonly EcsPoolInject<HitComponent> _hitPool = default;
        readonly EcsPoolInject<EffectAbilityRefComponent> _effectAbilityRefPool = default;

        readonly EcsFilterInject<Inc<BoardTag, BoardPositionComponent, OwnerComponent>> _boardFilter = default;
        readonly EcsPoolInject<BoardPositionComponent> _boardPosPool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;

        readonly EcsFilterInject<Inc<PlayerComponent>> _playerFilter = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;

        readonly EcsPoolInject<AbilitySourceComponent> _abilitySourcePool = default;
        readonly EcsPoolInject<NetworkEntityComponent> _netKeyPool = default;
        readonly EcsPoolInject<OwnCardTag> _ownCardPool = default;

        readonly EcsPoolInject<TargetShapeComponent> _shapePool = default;

        readonly EcsPoolInject<MoveSourceToCellEffectComponent> _moveSourceToCellPool = default;

        readonly EcsPoolInject<ColorRequirementComponent> _colorReqPool = default;
        readonly EcsPoolInject<RandomTargetCountComponent> _randomCountPool = default;
        readonly EcsPoolInject<RedTag>    _redTagPool    = default;
        readonly EcsPoolInject<BlueTag>   _blueTagPool   = default;
        readonly EcsPoolInject<GreenTag>  _greenTagPool  = default;
        readonly EcsPoolInject<YellowTag> _yellowTagPool = default;
        readonly EcsPoolInject<WhiteTag>  _whiteTagPool  = default;
        readonly EcsPoolInject<BlackTag>  _blackTagPool  = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var abilityEntity in _filter.Value)
            {
                ref var state = ref _chainStatePool.Value.Get(abilityEntity);
                int stepIndex = state.CurrentStepIndex;
                int ownerEntity = _resolvePool.Value.Get(abilityEntity).OwnerEntity;
                int ownerPlayerId = GetOwnerPlayerId(abilityEntity);
                bool hasProjectile = _projectileViewPool.Value.Has(abilityEntity);

                List<IAbilityEffect> effects = GetStepEffects(abilityEntity, stepIndex);

                // Условие шага N (ChainStep.Condition): если не выполнено — шаг пропускается.
                if (stepIndex > 0 && _chainContainerPool.Value.Has(abilityEntity))
                {
                    ref var chainContainer = ref _chainContainerPool.Value.Get(abilityEntity);
                    int idxCond = stepIndex - 1;
                    if (chainContainer.Steps != null && idxCond >= 0 && idxCond < chainContainer.Steps.Count)
                    {
                        var step = chainContainer.Steps[idxCond];
                        if (step?.Condition != null)
                        {
                            int srcCardForCond = _abilitySourcePool.Value.Has(abilityEntity)
                                ? _abilitySourcePool.Value.Get(abilityEntity).CardEntity : -1;
                            int ownerPlayerEntity = _state.Value.TryGetPlayerEntity(out int _pe) ? _pe : -1;
                            if (!step.Condition.Evaluate(_world.Value, abilityEntity, srcCardForCond, ownerPlayerId, ownerPlayerEntity))
                                effects = null; // условие не выполнено — шаг пуст, цепочка продвинется
                        }
                    }
                }

                List<int> targets = effects == null
                    ? new List<int>()
                    : ResolveStepTargets(abilityEntity, stepIndex, ownerEntity, ownerPlayerId, ref state);

                // Захват клетки на шаге 0 — для последующего «займи место»
                if (stepIndex == 0 && targets.Count > 0 && _boardPosPool.Value.Has(targets[0]))
                {
                    ref var pos = ref _boardPosPool.Value.Get(targets[0]);
                    state.HasCapturedCell = true;
                    state.CapturedRow = pos.Row;
                    state.CapturedCol = pos.Col;
                    state.CapturedCellOwnerId = pos.OwnerId;
                }

                // Обновляем «текущую» цель и дефолтную produced
                state.CurrentTargets = new List<int>(targets);
                if (targets.Count > 0)
                    state.ProducedEntity = targets[0];

                // Пустые шаги (нет эффектов) не нуждаются в репликации — пропускаем снэпшот.
                if (effects != null && effects.Count > 0)
                    EmitSnapshotIfOwn(abilityEntity, stepIndex, targets);

                if (effects != null)
                {
                    foreach (int targetEntity in targets)
                    {
                        foreach (var effect in effects)
                        {
                            int effectEntity = _world.Value.NewEntity();
                            _effectPool.Value.Add(effectEntity);

                            ref var ctx = ref _targetEntityPool.Value.Add(effectEntity);
                            ctx.TargetEntity = targetEntity;
                            ctx.OwnerEntity = ownerEntity;

                            ref var refComp = ref _effectAbilityRefPool.Value.Add(effectEntity);
                            refComp.AbilityEntity = abilityEntity;
                            refComp.StepIndex = stepIndex;

                            effect.AddEffect(_world.Value, effectEntity);

                            // Self-contained cell для MoveSourceToCell — чтобы пассивный
                            // клиент не зависел от ChainStateComponent (его там нет).
                            if (_moveSourceToCellPool.Value.Has(effectEntity) && state.HasCapturedCell)
                            {
                                ref var m = ref _moveSourceToCellPool.Value.Get(effectEntity);
                                m.HasCell = true;
                                m.Row = state.CapturedRow;
                                m.Col = state.CapturedCol;
                                m.OwnerId = state.CapturedCellOwnerId;
                            }

                            if (!hasProjectile)
                                _hitPool.Value.Add(effectEntity);
                        }
                    }
                }

                _needsStepResolvePool.Value.Del(abilityEntity);
            }
        }

        // ── Эффекты для текущего шага ────────────────────────────────────────
        List<IAbilityEffect> GetStepEffects(int abilityEntity, int stepIndex)
        {
            if (stepIndex == 0)
            {
                if (!_effectContainerPool.Value.Has(abilityEntity)) return null;
                return _effectContainerPool.Value.Get(abilityEntity).AbilityEffects;
            }

            if (!_chainContainerPool.Value.Has(abilityEntity)) return null;
            ref var c = ref _chainContainerPool.Value.Get(abilityEntity);
            int idx = stepIndex - 1;
            if (c.Steps == null || idx < 0 || idx >= c.Steps.Count) return null;
            var step = c.Steps[idx];
            if (step?.Effects == null || step.Effects.Count == 0) return null;

            var result = new List<IAbilityEffect>(step.Effects.Count);
            foreach (var e in step.Effects)
                result.Add(e);
            return result;
        }

        // ── Цели для текущего шага ────────────────────────────────────────────
        List<int> ResolveStepTargets(int abilityEntity, int stepIndex, int ownerEntity, int ownerPlayerId, ref ChainStateComponent state)
        {
            if (stepIndex == 0)
            {
                var targets0 = ResolveAbilityTargets(abilityEntity, ownerPlayerId, ownerEntity);

                var shape = _shapePool.Value.Has(abilityEntity)
                    ? _shapePool.Value.Get(abilityEntity).Shape
                    : TargetShape.Single;
                if (shape != TargetShape.Single)
                {
                    var mask = _targetFlagsPool.Value.Has(abilityEntity)
                        ? _targetFlagsPool.Value.Get(abilityEntity).Mask
                        : TargetMask.None;
                    targets0 = ExpandByShape(targets0, shape, mask, ownerPlayerId, ownerEntity);
                }
                return targets0;
            }

            // Шаг N > 0: источник цели — из ChainStep.TargetSource
            ref var chainContainer = ref _chainContainerPool.Value.Get(abilityEntity);
            var step = chainContainer.Steps[stepIndex - 1];

            var result = new List<int>();
            switch (step.TargetSource)
            {
                case ChainTargetSource.PreviousTarget:
                    if (state.CurrentTargets != null) result.AddRange(state.CurrentTargets);
                    break;

                case ChainTargetSource.PreviousProduced:
                    if (state.ProducedEntity >= 0) result.Add(state.ProducedEntity);
                    break;

                case ChainTargetSource.Source:
                case ChainTargetSource.PreviousCell:
                    int srcCard = GetSourceCardEntity(abilityEntity);
                    if (srcCard >= 0) result.Add(srcCard);
                    break;

                case ChainTargetSource.PickedCard:
                    if (state.PickedCardEntity >= 0) result.Add(state.PickedCardEntity);
                    break;
            }

            // Форма области для шага (если задана) — расширяем вокруг каждой цели.
            if (step.Shape != TargetShape.Single && result.Count > 0)
                result = ExpandByShape(result, step.Shape, step.TargetMaskOverride, ownerPlayerId, ownerEntity);

            return result;
        }

        int GetSourceCardEntity(int abilityEntity)
        {
            if (!_abilitySourcePool.Value.Has(abilityEntity)) return -1;
            return _abilitySourcePool.Value.Get(abilityEntity).CardEntity;
        }

        // ── Резолв целей шага 0 (TargetMask + Mode) ──────────────────────────
        List<int> ResolveAbilityTargets(int abilityEntity, int ownerPlayerId, int ownerEntity)
        {
            var result = new List<int>();

            if (_chosenTargetPool.Value.Has(abilityEntity))
            {
                int chosen = _chosenTargetPool.Value.Get(abilityEntity).TargetEntity;
                if (chosen != -1) { result.Add(chosen); return result; }
            }

            if (!_targetFlagsPool.Value.Has(abilityEntity)) return result;

            var flags = _targetFlagsPool.Value.Get(abilityEntity).Mask;
            bool isAll = flags.Has(TargetMask.All);
            bool isRandom = flags.Has(TargetMask.Random);
            bool excludeSelf = flags.Has(TargetMask.ExcludeSelf);

            // Self-only без All: возвращаем только владельца (быстрый путь).
            if (flags.Has(TargetMask.Self) && !isAll)
            {
                if (ownerEntity != -1) { result.Add(ownerEntity); return result; }
            }

            var candidates = new List<int>();

            // Self вместе с All — заходит как один из кандидатов.
            if (isAll && flags.Has(TargetMask.Self) && ownerEntity != -1)
                candidates.Add(ownerEntity);

            if (flags.Has(TargetMask.AllyPlayer) && _state.Value.TryGetPlayerEntity(out int playerEntity))
                candidates.Add(playerEntity);
            if (flags.Has(TargetMask.EnemyPlayer) && _state.Value.TryGetOpponentEntity(out int opponentEntity))
                candidates.Add(opponentEntity);

            bool targetEnemy = flags.Has(TargetMask.EnemyCreature);
            bool targetAlly = flags.Has(TargetMask.AllyCreature);

            if (targetEnemy || targetAlly)
                CollectCreaturesSorted(ownerPlayerId, targetEnemy, targetAlly, candidates);

            if (excludeSelf && ownerEntity != -1)
                candidates.Remove(ownerEntity);

            // Цветовой фильтр (только для существ — у игроков нет цветовых тегов).
            if (_colorReqPool.Value.Has(abilityEntity))
            {
                ref var req = ref _colorReqPool.Value.Get(abilityEntity);
                for (int i = candidates.Count - 1; i >= 0; i--)
                    if (!MatchesColor(candidates[i], req)) candidates.RemoveAt(i);
            }

            if (candidates.Count == 0) return result;

            if (isAll)
            {
                // Field-семантика: бьём всех подходящих.
                result.AddRange(candidates);
                return result;
            }

            if (isRandom)
            {
                int wanted = _randomCountPool.Value.Has(abilityEntity)
                    ? _randomCountPool.Value.Get(abilityEntity).Count : 1;
                if (wanted < 1) wanted = 1;
                wanted = System.Math.Min(wanted, candidates.Count);

                var rng = new System.Random(ComputeStableSeed(abilityEntity));
                // Fisher-Yates до wanted позиций — детерминированный отбор N разных.
                for (int i = 0; i < wanted; i++)
                {
                    int j = i + rng.Next(0, candidates.Count - i);
                    int tmp = candidates[i]; candidates[i] = candidates[j]; candidates[j] = tmp;
                    result.Add(candidates[i]);
                }
            }
            else
            {
                result.Add(candidates[0]);
            }

            return result;
        }

        bool MatchesColor(int entity, in ColorRequirementComponent req)
        {
            // Игроки без цвета — пропускаем фильтр (нет смысла отсеивать игрока по цвету).
            if (_playerPool.Value.Has(entity)) return true;

            var colors = ColorsOf(entity);

            if (req.Forbidden != 0 && (colors & req.Forbidden) != 0)
                return false;

            if (req.Required != 0)
            {
                if (req.AnyRequired) { if ((colors & req.Required) == 0) return false; }
                else                  { if ((colors & req.Required) != req.Required) return false; }
            }

            return true;
        }

        EnumService.Element ColorsOf(int entity)
        {
            EnumService.Element c = 0;
            if (_redTagPool   .Value.Has(entity)) c |= EnumService.Element.Red;
            if (_blueTagPool  .Value.Has(entity)) c |= EnumService.Element.Blue;
            if (_greenTagPool .Value.Has(entity)) c |= EnumService.Element.Green;
            if (_yellowTagPool.Value.Has(entity)) c |= EnumService.Element.Yellow;
            if (_whiteTagPool .Value.Has(entity)) c |= EnumService.Element.White;
            if (_blackTagPool .Value.Has(entity)) c |= EnumService.Element.Black;
            return c;
        }

        void CollectCreaturesSorted(int ownerPlayerId, bool includeEnemy, bool includeAlly, List<int> output)
        {
            var list = new List<(int row, int col, int entity)>();

            foreach (var creatureEntity in _boardFilter.Value)
            {
                ref var pos = ref _boardPosPool.Value.Get(creatureEntity);
                ref var owner = ref _ownerPool.Value.Get(creatureEntity);
                bool isAlly = owner.OwnerId == ownerPlayerId;

                if (isAlly && !includeAlly) continue;
                if (!isAlly && !includeEnemy) continue;

                list.Add((pos.Row, pos.Col, creatureEntity));
            }

            list.Sort((a, b) => a.row != b.row ? a.row.CompareTo(b.row) : a.col.CompareTo(b.col));

            foreach (var item in list)
                output.Add(item.entity);
        }

        List<int> ExpandByShape(List<int> anchors, TargetShape shape, TargetMask mask, int ownerPlayerId, int ownerEntity)
        {
            var result = new List<int>();
            var seen = new HashSet<int>();

            foreach (int anchor in anchors)
            {
                if (anchor < 0) continue;
                if (!_boardPosPool.Value.Has(anchor) && seen.Add(anchor))
                    result.Add(anchor);
            }

            var creatures = new List<(int row, int col, int entity)>();
            foreach (int anchor in anchors)
            {
                if (!_boardPosPool.Value.Has(anchor)) continue;
                ref var pos = ref _boardPosPool.Value.Get(anchor);
                CollectShapeCreatures(pos.Row, pos.Col, pos.OwnerId, shape, mask, ownerPlayerId, ownerEntity, seen, creatures);
            }

            creatures.Sort((a, b) => a.row != b.row ? a.row.CompareTo(b.row) : a.col.CompareTo(b.col));
            foreach (var c in creatures) result.Add(c.entity);

            return result;
        }

        void CollectShapeCreatures(int ar, int ac, int owner, TargetShape shape, TargetMask mask,
                                   int ownerPlayerId, int ownerEntity, HashSet<int> seen,
                                   List<(int, int, int)> output)
        {
            bool filterByAllegiance = mask.TargetsCreature();
            bool excludeSelf = mask.Has(TargetMask.ExcludeSelf);

            foreach (var ce in _boardFilter.Value)
            {
                ref var cpos = ref _boardPosPool.Value.Get(ce);
                if (cpos.OwnerId != owner) continue;
                if (!InShape(ar, ac, cpos.Row, cpos.Col, shape)) continue;
                if (excludeSelf && ce == ownerEntity) continue;

                if (filterByAllegiance)
                {
                    bool isAlly = _ownerPool.Value.Get(ce).OwnerId == ownerPlayerId;
                    if (!mask.MatchesCreature(isAlly)) continue;
                }

                if (seen.Add(ce)) output.Add((cpos.Row, cpos.Col, ce));
            }
        }

        static bool InShape(int ar, int ac, int r, int c, TargetShape shape)
        {
            switch (shape)
            {
                case TargetShape.Row:    return r == ar;
                case TargetShape.Column: return c == ac;
                case TargetShape.Cross:
                    return (r == ar && c == ac)
                        || (r == ar && System.Math.Abs(c - ac) == 1)
                        || (c == ac && System.Math.Abs(r - ar) == 1);
                case TargetShape.Adjacent:
                    return System.Math.Abs(r - ar) <= 1 && System.Math.Abs(c - ac) <= 1;
                default:
                    return r == ar && c == ac;
            }
        }

        // ── Снэпшот резолва: per-step активный → пассивный ──────────────────
        void EmitSnapshotIfOwn(int abilityEntity, int stepIndex, List<int> targets)
        {
            if (!_abilitySourcePool.Value.Has(abilityEntity)) return;
            ref var src = ref _abilitySourcePool.Value.Get(abilityEntity);
            int cardEntity = src.CardEntity;
            if (cardEntity < 0) return;

            if (!_ownCardPool.Value.Has(cardEntity)) return;

            string cardKey = _netKeyPool.Value.Has(cardEntity)
                ? _netKeyPool.Value.Get(cardEntity).NetworkEntityKey
                : null;

            var keys = new string[targets.Count];
            for (int i = 0; i < targets.Count; i++)
                keys[i] = KeyForTarget(targets[i]);

            // Захваченная клетка — для MoveSourceToCell на пассивной стороне.
            bool hasCell = false;
            int capRow = 0, capCol = 0, capOwner = 0;
            if (_chainStatePool.Value.Has(abilityEntity))
            {
                ref var state = ref _chainStatePool.Value.Get(abilityEntity);
                hasCell = state.HasCapturedCell;
                capRow = state.CapturedRow;
                capCol = state.CapturedCol;
                capOwner = state.CapturedCellOwnerId;
            }

            GameEventBus.Publish(new AbilityResolvedNetEvent
            {
                SourceCardEntity     = cardEntity,
                SourceCardNetworkKey = cardKey,
                AbilityIndex         = src.AbilityIndex,
                StepIndex            = stepIndex,
                TargetKeys           = keys,
                HasCapturedCell      = hasCell,
                CapturedRow          = capRow,
                CapturedCol          = capCol,
                CapturedCellOwnerId  = capOwner,
            });
        }

        string KeyForTarget(int entity)
        {
            if (entity < 0) return null;
            if (_netKeyPool.Value.Has(entity))
                return _netKeyPool.Value.Get(entity).NetworkEntityKey;
            if (_playerPool.Value.Has(entity))
                return "PLAYER:" + _playerPool.Value.Get(entity).PlayerId;
            return null;
        }

        int GetOwnerPlayerId(int abilityEntity)
        {
            if (_state.Value.TryGetPlayerEntity(out int playerEntity))
                return _playerPool.Value.Get(playerEntity).PlayerId;
            return -1;
        }

        int ComputeStableSeed(int abilityEntity)
        {
            string key = null;
            int abilityIndex = 0;

            if (_abilitySourcePool.Value.Has(abilityEntity))
            {
                ref var src = ref _abilitySourcePool.Value.Get(abilityEntity);
                abilityIndex = src.AbilityIndex;
                if (src.CardEntity >= 0 && _netKeyPool.Value.Has(src.CardEntity))
                    key = _netKeyPool.Value.Get(src.CardEntity).NetworkEntityKey;
            }

            if (string.IsNullOrEmpty(key))
                return abilityEntity;

            unchecked
            {
                const uint prime = 16777619;
                uint hash = 2166136261;
                for (int i = 0; i < key.Length; i++)
                {
                    hash ^= (uint)key[i];
                    hash *= prime;
                }
                hash ^= (uint)abilityIndex;
                hash *= prime;
                return (int)hash;
            }
        }
    }
}
