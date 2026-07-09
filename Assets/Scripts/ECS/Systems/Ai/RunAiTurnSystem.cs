using System.Collections.Generic;
using Game.Core.Configs;
using Game.Core.Ecs.Components;
using Game.Core.Service;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using UnityEngine;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// PvE-мозг v2 — utility AI: каждое возможное действие получает численную полезность из состояния
    /// доски, выполняется лучшее. Одно действие в ActionInterval сек (человек видит ход).
    ///
    /// КАРТЫ: полезность из АВТО-ролей эффектов (IEffect.AiRole — выводятся из кирпичей карты, ручной
    /// разметки не нужно) + контекста: AoE ждёт скопления врагов, бафф требует своих существ, дискард —
    /// карт у человека, добор — пустеющей руки и т.д. AoE-ность = AbilityFieldComponent на способности.
    /// СУЩЕСТВА: атаки оцениваются по размену (килл > лицо > обмен > невыгодно), движение — по прогрессу
    /// к аватару человека со сменой колонки (обход своих/невыгодных блоков).
    /// ПРАВИЛА = как у ввода человека (RunSelectCellSystem): шаг/атака только на СОСЕДНЮЮ клетку
    /// (вбок/назад/вперёд, фронт пересекается row1→row1), атака ≤ MaxAttacksPerTurn — ИИ сам ведёт
    /// AttacksUsedComponent (Move/AttackSystem легальность НЕ проверяют — replay-authoritative).
    ///
    /// РЕГИСТРАЦИЯ: ПЕРВЫМ в _abilitySystems (см. гонку DelHere — однокадровые события должны родиться
    /// раньше потребителей в кадре). Move/AttackRequest не в DelHere — переживают кадр.
    /// </summary>
    public sealed class RunAiTurnSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;

        readonly EcsFilterInject<Inc<PlayerComponent, AiPlayerComponent>> _aiFilter = default;
        readonly EcsFilterInject<Inc<PlayerComponent, LocalComponent>> _humanFilter = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsPoolInject<ActiveState> _activePool = default;
        readonly EcsPoolInject<GoldComponent> _goldPool = default;
        readonly EcsPoolInject<ManaComponent> _manaPool = default;
        readonly EcsPoolInject<HealthComponent> _healthPool = default;
        readonly EcsPoolInject<HandComponent> _handPool = default;
        readonly EcsPoolInject<TurnCounterComponent> _turnCounterPool = default;

        // Рука ИИ
        readonly EcsFilterInject<Inc<HandTag, OwnerComponent, CardModelComponent>> _handCards = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;
        readonly EcsPoolInject<GoldCostComponent> _goldCostPool = default;
        readonly EcsPoolInject<ManaCostComponent> _manaCostPool = default;
        readonly EcsPoolInject<HealthCostComponent> _healthCostPool = default;
        readonly EcsPoolInject<CommanderCooldownComponent> _commanderCdPool = default;
        readonly EcsPoolInject<RequestCardCastEvent> _castReqPool = default;
        readonly EcsPoolInject<ForceRandomTargetingComponent> _forceRandomPool = default;
        readonly EcsPoolInject<CreatureTag> _creatureTagPool = default;

        // Роли эффектов карты (utility-оценка)
        readonly EcsPoolInject<AbilityContainerComponent> _abilityContainerPool = default;
        readonly EcsPoolInject<AbilityEffectContainerComponent> _abilityEffectsPool = default;
        readonly EcsPoolInject<AbilityFieldComponent> _abilityFieldPool = default;

        // Борд
        readonly EcsFilterInject<Inc<CreatureTag, BoardTag, BoardPositionComponent, SpeedComponent, OwnerComponent>, Exc<DeadTag>> _boardCreatures = default;
        readonly EcsPoolInject<BoardPositionComponent> _posPool = default;
        readonly EcsPoolInject<SpeedComponent> _speedPool = default;
        readonly EcsPoolInject<AttackComponent> _attackValuePool = default;
        readonly EcsPoolInject<MoveRequestEvent> _movePool = default;
        readonly EcsPoolInject<AttackRequestEvent> _attackPool = default;
        readonly EcsPoolInject<MovingTag> _movingPool = default;
        readonly EcsPoolInject<AttacksUsedComponent> _attacksUsedPool = default;

        // Размещение существа (карта ждёт клетку)
        readonly EcsFilterInject<Inc<PendingSelectCellState>> _pendingCell = default;
        readonly EcsPoolInject<CellClickEvent> _clickPool = default;

        // Конец хода
        readonly EcsPoolInject<EndTurnRequestEvent> _endReqPool = default;

        // «Пайплайн осел» — не действуем, пока крутятся способности/движение/анимации/касты.
        readonly EcsFilterInject<Inc<AbilityCastEvent>> _abilityCast = default;
        readonly EcsFilterInject<Inc<AbilityTargetingState>> _abilityTargeting = default;
        readonly EcsFilterInject<Inc<AbilityQueuedState>> _abilityQueued = default;
        readonly EcsFilterInject<Inc<AbilityCastPendingComponent>> _abilityFlight = default;
        readonly EcsFilterInject<Inc<RequestCardCastEvent>> _castRequests = default;
        readonly EcsFilterInject<Inc<CastEvent>> _castsInProgress = default;
        readonly EcsFilterInject<Inc<MovingTag>> _moving = default;
        readonly EcsFilterInject<Inc<AttackAnimPendingTag>> _attackAnim = default;
        readonly EcsFilterInject<Inc<PendingOnCastComponent>> _pendingOnCast = default;

        const int FrontRow = 0;
        const int BackRow = 1;
        const int Cols = 5;
        const int MaxActionsPerTurn = 60;      // предохранитель от зацикливания
        const int MaxAttacksPerTurn = 1;       // как в RunSelectCellSystem
        const int PlayThreshold = 5;           // карту играем, только если полезность ≥ порога
        static readonly int[] ColOrder = { 2, 1, 3, 0, 4 };   // тай-брейк размещения: центр → наружу

        float _nextActionTime;
        float _interval = 0.9f;
        bool _intervalLoaded;
        bool _turnInProgress;
        int _actionsThisTurn;
        readonly HashSet<long> _attempted = new();   // (entity, kind, detail) — не долбим отклонённое весь ход

        enum Kind { Cast = 1, Move = 2, Attack = 3 }

        public void Run(IEcsSystems systems)
        {
            if (!PveMode.Enabled || MatchState.IsOver) return;

            int ai = -1;
            foreach (var e in _aiFilter.Value) { ai = e; break; }
            if (ai < 0) return;

            if (!_activePool.Value.Has(ai))
            {
                _turnInProgress = false;
                return;
            }

            if (!_turnInProgress)
            {
                _turnInProgress = true;
                _actionsThisTurn = 0;
                _attempted.Clear();
                LoadInterval();
                _nextActionTime = Time.time + _interval;
                Debug.Log("[AI] мой ход — думаю…");
                return;
            }

            if (PipelineBusy()) return;
            if (Time.time < _nextActionTime) return;

            bool acted = _actionsThisTurn < MaxActionsPerTurn && TryAct(ai);
            _actionsThisTurn++;
            _nextActionTime = Time.time + _interval;

            if (!acted)
            {
                if (!_endReqPool.Value.Has(ai)) _endReqPool.Value.Add(ai);
                Debug.Log($"[AI] действий больше нет → конец хода (actions={_actionsThisTurn - 1})");
            }
        }

        bool PipelineBusy()
            => _abilityCast.Value.GetEntitiesCount() > 0
            || _abilityTargeting.Value.GetEntitiesCount() > 0
            || _abilityQueued.Value.GetEntitiesCount() > 0
            || _abilityFlight.Value.GetEntitiesCount() > 0
            || _castRequests.Value.GetEntitiesCount() > 0
            || _castsInProgress.Value.GetEntitiesCount() > 0
            || _moving.Value.GetEntitiesCount() > 0
            || _attackAnim.Value.GetEntitiesCount() > 0
            || _pendingOnCast.Value.GetEntitiesCount() > 0;

        // ── контекст хода (пересобирается на каждое действие — доска меняется) ─────────

        struct Ctx
        {
            public int Ai, Human;             // сущности игроков
            public int AiId, HumanId;         // PlayerId
            public int MyBoard, EnemyBoard;   // существ на доске (ИИ/человек)
            public int EnemyMaxAtk;           // самая сильная атака человека
            public int AiHand, HumanHand;     // карт в руках
            public int PersonalTurn;          // личный ход ИИ
        }

        Ctx BuildCtx(int ai)
        {
            var c = new Ctx { Ai = ai, Human = -1, HumanId = -1 };
            c.AiId = _playerPool.Value.Get(ai).PlayerId;
            foreach (var e in _humanFilter.Value) { c.Human = e; c.HumanId = _playerPool.Value.Get(e).PlayerId; break; }

            foreach (var e in _boardCreatures.Value)
            {
                int owner = _ownerPool.Value.Get(e).OwnerId;
                if (owner == c.AiId) c.MyBoard++;
                else
                {
                    c.EnemyBoard++;
                    int atk = _attackValuePool.Value.Has(e) ? _attackValuePool.Value.Get(e).Value : 0;
                    if (atk > c.EnemyMaxAtk) c.EnemyMaxAtk = atk;
                }
            }

            c.AiHand = _handPool.Value.Has(ai) ? _handPool.Value.Get(ai).Count : 0;
            c.HumanHand = c.Human >= 0 && _handPool.Value.Has(c.Human) ? _handPool.Value.Get(c.Human).Count : 0;
            c.PersonalTurn = _turnCounterPool.Value.Has(ai) ? _turnCounterPool.Value.Get(ai).Personal : 1;
            return c;
        }

        // ── главный выбор действия ──────────────────────────────────────────────────────

        bool TryAct(int ai)
        {
            var ctx = BuildCtx(ai);

            // 1) Существо ждёт клетку → лучшая клетка фронта (блок угрожаемых колонок / свободная линия).
            foreach (var card in _pendingCell.Value)
            {
                if (!_ownerPool.Value.Has(card) || _ownerPool.Value.Get(card).OwnerId != ctx.AiId) continue;
                int col = BestPlacementCol(ctx);
                int clickEntity = _world.Value.NewEntity();
                ref var click = ref _clickPool.Value.Add(clickEntity);
                click.Row = col >= 0 ? FrontRow : -1;   // нет клетки → «клик мимо» = отмена с рефандом
                click.Col = col >= 0 ? col : 0;
                click.OwnerId = ctx.AiId;
                Debug.Log($"[AI] размещаю существо card={card} col={col}");
                return true;
            }

            // 2) Лучшая карта руки по полезности (roles + контекст); играем только если ≥ порога.
            int bestCard = -1, bestScore = PlayThreshold - 1;
            foreach (var card in _handCards.Value)
            {
                if (_ownerPool.Value.Get(card).OwnerId != ctx.AiId) continue;
                if (_commanderCdPool.Value.Has(card)) continue;
                if (!Affordable(card, ai)) continue;
                if (_attempted.Contains(AttemptKey(card, Kind.Cast, 0))) continue;

                int score = ScoreCard(card, ctx);
                if (score > bestScore) { bestScore = score; bestCard = card; }
            }
            if (bestCard >= 0)
            {
                _attempted.Add(AttemptKey(bestCard, Kind.Cast, 0));
                if (!_forceRandomPool.Value.Has(bestCard)) _forceRandomPool.Value.Add(bestCard);   // Selected → Random
                if (!_castReqPool.Value.Has(bestCard))
                {
                    ref var req = ref _castReqPool.Value.Add(bestCard);
                    req.CardEntity = bestCard;
                    req.Free = false;   // ИИ платит как игрок
                }
                Debug.Log($"[AI] играю карту card={bestCard} (score={bestScore})");
                return true;
            }

            // 3) Лучшее действие существ (атака/шаг) по всем существам сразу.
            return TryBestCreatureAction(ctx);
        }

        // ── оценка карт ─────────────────────────────────────────────────────────────────

        int ScoreCard(int card, in Ctx ctx)
        {
            int cost = CardCost(card);

            // Существо: тело на доску. Ценим выше при отставании по доске; без клетки — не играем.
            if (_creatureTagPool.Value.Has(card))
            {
                if (FindFreeFrontCol(ctx.AiId) < 0) return -100;
                int s = 8 + cost * 2;
                if (ctx.MyBoard < ctx.EnemyBoard) s += 5;
                if (ctx.MyBoard == 0) s += 4;
                return s;
            }

            // Спелл/чарм: лучшая роль среди эффектов его способностей.
            int best = 0;
            bool any = false;
            if (_abilityContainerPool.Value.Has(card))
            {
                var abilities = _abilityContainerPool.Value.Get(card).AbilityEntities;
                if (abilities != null)
                {
                    foreach (var abilityEntity in abilities)
                    {
                        if (!_abilityEffectsPool.Value.Has(abilityEntity)) continue;
                        bool aoe = _abilityFieldPool.Value.Has(abilityEntity);
                        var effects = _abilityEffectsPool.Value.Get(abilityEntity).Effects;
                        if (effects == null) continue;
                        foreach (var effect in effects)
                        {
                            if (effect == null) continue;
                            int s = ScoreRole(effect.AiRole, aoe, cost, ctx);
                            if (!any || s > best) { best = s; any = true; }
                        }
                    }
                }
            }
            return any ? best : 3 + cost;   // без способностей — оценка по стоимости
        }

        int ScoreRole(AiEffectRole role, bool aoe, int cost, in Ctx ctx)
        {
            switch (role)
            {
                case AiEffectRole.Damage:
                    if (aoe) return ctx.EnemyBoard >= 2 ? 6 + 4 * ctx.EnemyBoard : (ctx.EnemyBoard > 0 ? 6 : 1);
                    return ctx.EnemyBoard > 0 ? 8 + cost : 3;
                case AiEffectRole.Removal:
                    return ctx.EnemyBoard > 0 ? 8 + cost * 2 + (ctx.EnemyMaxAtk >= 3 ? 4 : 0) : 0;
                case AiEffectRole.BuffAlly:
                    return ctx.MyBoard > 0 ? 6 + cost * 2 : -50;
                case AiEffectRole.DebuffEnemy:
                    return ctx.EnemyBoard > 0 ? 6 + cost * 2 : -50;
                case AiEffectRole.Summon:
                    return FindFreeFrontCol(ctx.AiId) >= 0 ? 8 + cost * 2 : -50;
                case AiEffectRole.Draw:
                    return ctx.AiHand <= 3 ? 8 + cost : 4;
                case AiEffectRole.Heal:
                    return HealUseful(ctx) ? 5 + cost : 2;
                case AiEffectRole.Resource:
                    return ctx.PersonalTurn <= 4 ? 6 : 3;
                case AiEffectRole.HandDisruption:
                    return ctx.HumanHand > 0 ? 7 + cost : 0;
                case AiEffectRole.Curse:
                    return 7 + cost;
                default:
                    return 4 + cost;
            }
        }

        bool HealUseful(in Ctx ctx)
        {
            if (!_healthPool.Value.Has(ctx.Ai)) return ctx.MyBoard > 0;
            ref var hp = ref _healthPool.Value.Get(ctx.Ai);
            return hp.Current < hp.Max * 0.7f || ctx.MyBoard > 0;
        }

        // ── размещение: блокировать угрожаемые колонки, иначе свободная линия ───────────

        int BestPlacementCol(in Ctx ctx)
        {
            int bestCol = -1, bestScore = int.MinValue;
            foreach (var col in ColOrder)
            {
                if (CreatureAt(FrontRow, col, ctx.AiId) >= 0) continue;   // занято

                int score = 1;
                bool humanInCol = ColumnHasCreatureOf(col, ctx.HumanId);
                bool mineInCol  = ColumnHasCreatureOf(col, ctx.AiId);
                if (humanInCol && !mineInCol) score = 8;        // блок/контест незакрытой угрозы
                else if (!humanInCol && !mineInCol) score = 4;  // свободная линия — давим
                if (score > bestScore) { bestScore = score; bestCol = col; }
            }
            return bestCol;
        }

        bool ColumnHasCreatureOf(int col, int ownerId)
        {
            if (ownerId < 0) return false;
            foreach (var e in _boardCreatures.Value)
            {
                ref var p = ref _posPool.Value.Get(e);
                if (p.Col != col) continue;
                if (_ownerPool.Value.Get(e).OwnerId == ownerId) return true;
            }
            return false;
        }

        // ── существа: лучшее действие (атака по размену / шаг с прогрессом и сменой колонки) ──

        bool TryBestCreatureAction(in Ctx ctx)
        {
            int bestCreature = -1, bestScore = 0;
            int bestKind = 0, bestTarget = -1, bestRow = 0, bestCol = 0, bestOwner = 0;

            foreach (var creature in _boardCreatures.Value)
            {
                if (_ownerPool.Value.Get(creature).OwnerId != ctx.AiId) continue;
                if (_movingPool.Value.Has(creature)) continue;
                if (_speedPool.Value.Get(creature).Remaining <= 0) continue;

                ref var pos = ref _posPool.Value.Get(creature);
                bool canAttack = AttacksUsed(creature) < MaxAttacksPerTurn;
                int myAtk = _attackValuePool.Value.Has(creature) ? _attackValuePool.Value.Get(creature).Value : 0;
                int myHp  = _healthPool.Value.Has(creature) ? _healthPool.Value.Get(creature).Current : 1;

                // Атака аватара: только с фронт-ряда человека (его row 0).
                if (canAttack && pos.OwnerId == ctx.HumanId && pos.Row == FrontRow && ctx.Human >= 0
                    && !_attempted.Contains(AttemptKey(creature, Kind.Attack, ctx.Human)))
                {
                    if (22 > bestScore)
                    { bestScore = 22; bestCreature = creature; bestKind = (int)Kind.Attack; bestTarget = ctx.Human; }
                }

                foreach (var (nr, nc, no) in Neighbours(pos.Row, pos.Col, pos.OwnerId, ctx.AiId, ctx.HumanId))
                {
                    int occupant = CreatureAt(nr, nc, no);

                    if (occupant >= 0)
                    {
                        // Враг на соседней клетке → оценка размена.
                        if (_ownerPool.Value.Get(occupant).OwnerId != ctx.AiId && canAttack
                            && !_attempted.Contains(AttemptKey(creature, Kind.Attack, occupant)))
                        {
                            int tHp  = _healthPool.Value.Has(occupant) ? _healthPool.Value.Get(occupant).Current : 1;
                            int tAtk = _attackValuePool.Value.Has(occupant) ? _attackValuePool.Value.Get(occupant).Value : 0;

                            int s;
                            if (myAtk >= tHp)            s = 26 + tAtk * 2;   // килл: чем опаснее цель, тем ценнее
                            else if (tAtk >= myHp)       s = 3;               // невыгодно: не убиваю, а меня убьют
                            else                          s = 8;               // просто обмен уроном
                            if (s > bestScore)
                            { bestScore = s; bestCreature = creature; bestKind = (int)Kind.Attack; bestTarget = occupant; }
                        }
                        continue;   // клетка занята — шаг невозможен
                    }

                    // Свободная клетка → шаг. Прогресс к аватару человека + смена колонки при блоке.
                    long move = AttemptKey(creature, Kind.Move, no * 100 + nr * 10 + nc);
                    if (_attempted.Contains(move)) continue;

                    int progress = DistToAvatar(pos.Row, pos.OwnerId, ctx) - DistToAvatar(nr, no, ctx);
                    int s2 = progress * 10 + 1;                       // вперёд 11, вбок 1, назад -9
                    if (progress == 0 && ForwardBlockedByAlly(pos, ctx)) s2 += 7;   // смена колонки при своём блоке
                    if (s2 <= 0) continue;

                    if (s2 > bestScore)
                    { bestScore = s2; bestCreature = creature; bestKind = (int)Kind.Move; bestRow = nr; bestCol = nc; bestOwner = no; }
                }
            }

            if (bestCreature < 0) return false;

            if (bestKind == (int)Kind.Attack)
            {
                _attempted.Add(AttemptKey(bestCreature, Kind.Attack, bestTarget));
                MarkAttacked(bestCreature);   // паритет с вводом человека: лимит атак ведём сами
                if (!_attackPool.Value.Has(bestCreature)) _attackPool.Value.Add(bestCreature).TargetEntity = bestTarget;
                Debug.Log($"[AI] существо {bestCreature} атакует {(bestTarget == ctx.Human ? "аватара" : bestTarget.ToString())} (score={bestScore})");
            }
            else
            {
                _attempted.Add(AttemptKey(bestCreature, Kind.Move, bestOwner * 100 + bestRow * 10 + bestCol));
                if (!_movePool.Value.Has(bestCreature))
                {
                    ref var mv = ref _movePool.Value.Add(bestCreature);
                    mv.ToRow = bestRow; mv.ToCol = bestCol; mv.ToOwnerId = bestOwner;
                }
                Debug.Log($"[AI] существо {bestCreature} идёт на ({bestRow},{bestCol},owner{bestOwner}) (score={bestScore})");
            }
            return true;
        }

        /// <summary>Дистанция до аватара человека: свой row0=3, свой row1=2, чужой row1=1, чужой row0=0.</summary>
        int DistToAvatar(int row, int owner, in Ctx ctx)
            => owner == ctx.AiId ? (row == FrontRow ? 3 : 2) : (row == BackRow ? 1 : 0);

        bool ForwardBlockedByAlly(in BoardPositionComponent pos, in Ctx ctx)
        {
            // «Вперёд» из текущей клетки (та же логика, что Neighbours): row0→row1 своей, row1→row1 чужой.
            int fr, fo;
            if (pos.OwnerId == ctx.AiId) { fr = pos.Row == FrontRow ? BackRow : BackRow; fo = pos.Row == FrontRow ? ctx.AiId : ctx.HumanId; }
            else                          { fr = FrontRow; fo = ctx.HumanId; }
            int occ = CreatureAt(fr, pos.Col, fo);
            return occ >= 0 && _ownerPool.Value.Get(occ).OwnerId == ctx.AiId;
        }

        /// <summary>Соседи клетки — зеркало GetNeighbours из RunSelectCellSystem (правила ввода человека).</summary>
        IEnumerable<(int row, int col, int owner)> Neighbours(int row, int col, int owner, int aiId, int humanId)
        {
            int other = owner == aiId ? humanId : aiId;
            if (col > 0) yield return (row, col - 1, owner);
            if (col < Cols - 1) yield return (row, col + 1, owner);
            if (row > 0) yield return (row - 1, col, owner);
            if (row < BackRow) yield return (row + 1, col, owner);
            else if (other >= 0) yield return (BackRow, col, other);   // пересечение фронта row1→row1
        }

        // ── helpers ─────────────────────────────────────────────────────────────────────

        static long AttemptKey(int entity, Kind kind, int detail)
            => ((long)entity << 32) | ((long)kind << 28) | (uint)detail;

        int AttacksUsed(int e) => _attacksUsedPool.Value.Has(e) ? _attacksUsedPool.Value.Get(e).Value : 0;

        void MarkAttacked(int e)
        {
            if (!_attacksUsedPool.Value.Has(e)) _attacksUsedPool.Value.Add(e);
            _attacksUsedPool.Value.Get(e).Value++;
        }

        int CardCost(int card)
        {
            if (_goldCostPool.Value.Has(card)) return _goldCostPool.Value.Get(card).Cost;
            if (_manaCostPool.Value.Has(card)) return _manaCostPool.Value.Get(card).Cost;
            if (_healthCostPool.Value.Has(card)) return _healthCostPool.Value.Get(card).Cost;
            return 0;
        }

        bool Affordable(int card, int aiPlayer)
        {
            if (_goldCostPool.Value.Has(card))
                return _goldPool.Value.Has(aiPlayer) &&
                       _goldPool.Value.Get(aiPlayer).Current >= CostModifierUtil.Effective(_world.Value, aiPlayer, _goldCostPool.Value.Get(card).Cost);
            if (_manaCostPool.Value.Has(card))
                return _manaPool.Value.Has(aiPlayer) &&
                       _manaPool.Value.Get(aiPlayer).Current >= CostModifierUtil.Effective(_world.Value, aiPlayer, _manaCostPool.Value.Get(card).Cost);
            if (_healthCostPool.Value.Has(card))
                return _healthPool.Value.Has(aiPlayer) &&
                       _healthPool.Value.Get(aiPlayer).Current > CostModifierUtil.Effective(_world.Value, aiPlayer, _healthCostPool.Value.Get(card).Cost);
            return true;
        }

        int FindFreeFrontCol(int aiId)
        {
            foreach (var col in ColOrder)
                if (CreatureAt(FrontRow, col, aiId) < 0) return col;
            return -1;
        }

        int CreatureAt(int row, int col, int ownerId)
        {
            foreach (var e in _boardCreatures.Value)
            {
                ref var p = ref _posPool.Value.Get(e);
                if (p.Row == row && p.Col == col && p.OwnerId == ownerId) return e;
            }
            return -1;
        }

        void LoadInterval()
        {
            if (_intervalLoaded) return;
            _intervalLoaded = true;
            var enc = PveEncounterLocator.Current;
            if (enc != null) _interval = Mathf.Max(0.1f, enc.ActionInterval);
        }
    }
}
