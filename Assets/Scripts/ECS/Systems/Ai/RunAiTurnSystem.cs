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
    /// СУЩЕСТВА: сначала ЛЕТАЛ-МАРШ (TryLethalRush: суммарное лицо всех, кто стоит на row 0 человека ИЛИ
    /// успевает дойти+ударить в этот ход, с раздачей клеток row 0 — сумма ≥ HP → турбо-режим в лицо);
    /// дальше атаки по размену (килл > фокус-файр вскладчину > лицо > обмен > невыгодно; охота на
    /// генераторов человека, вежливость к его хрипам), движение — только ОСМЫСЛЕННОЕ: прогресс к аватару,
    /// обход своего блока, подход на добивание, перекрытие колонны прорыва (и НЕ-уход со стены),
    /// безопасность по КАРТЕ УГРОЗ следующего хода человека (BuildThreatMap: не вставать под смерть,
    /// уходить из-под добивания в любую сторону, hit-and-run после потраченной атаки).
    /// Боковые шаги «ради прогулки» не делаются (база 0).
    /// ТАРГЕТИНГ КАРТ: не random — AiTargetPreferenceComponent по доминирующей роли эффектов (removal →
    /// самая опасная цель, урон → добивание, лечение → раненое, бафф → лучший носитель); КОМБО-ПОРЯДОК:
    /// рампа первой (если открывает недоступную карту), тело раньше баффа из руки (enabler-пары ролей).
    /// ПОЗИЦИОНКА (просьба пользователя 2026-07-19): призыв закрывает клетки нашего row 0, куда враг
    /// реально дотягивается за ход (EnemyReach — с них бьётся наш аватар); ТЕМПЕРАМЕНТ существ по триггерам
    /// (TemperOf, авто без разметки): турновый триггер → Hider (прячем в тыл, не шлём в размен); «при
    /// смерти» с полезным эффектом → Kamikaze (нарывается: лезет под удары, охотно идёт в смертельный
    /// размен, чтобы хрип сдетонировал).
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
        readonly EcsPoolInject<AiTargetPreferenceComponent> _aiPrefPool = default;
        readonly EcsPoolInject<CreatureTag> _creatureTagPool = default;

        // Роли эффектов карты (utility-оценка)
        readonly EcsPoolInject<AbilityContainerComponent> _abilityContainerPool = default;
        readonly EcsPoolInject<AbilityEffectContainerComponent> _abilityEffectsPool = default;
        readonly EcsPoolInject<AbilityFieldComponent> _abilityFieldPool = default;
        readonly EcsPoolInject<AbilityTriggerContainerComponent> _abilityTriggersPool = default;   // турновые триггеры → «ресурсные» существа

        // Борд
        readonly EcsFilterInject<Inc<CreatureTag, BoardTag, BoardPositionComponent, SpeedComponent, OwnerComponent>, Exc<DeadTag>> _boardCreatures = default;
        readonly EcsPoolInject<BoardPositionComponent> _posPool = default;
        readonly EcsPoolInject<SpeedComponent> _speedPool = default;
        readonly EcsPoolInject<AttackComponent> _attackValuePool = default;
        readonly EcsPoolInject<MoveRequestEvent> _movePool = default;
        readonly EcsPoolInject<AttackRequestEvent> _attackPool = default;
        readonly EcsPoolInject<MovingTag> _movingPool = default;
        readonly EcsPoolInject<AttacksUsedComponent> _attacksUsedPool = default;
        readonly EcsPoolInject<DoubleAttackTag> _doubleAttackPool = default;
        readonly EcsPoolInject<TauntTag> _tauntPool = default;
        readonly EcsPoolInject<StealthComponent> _stealthPool = default;

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
        readonly EcsFilterInject<Inc<ChainStateComponent>> _chainResolving = default;   // цепочка (RunChainSystem) в процессе
        readonly EcsFilterInject<Inc<DeathAnimPendingTag>> _deathAnim = default;         // существо ещё доигрывает анимацию смерти

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
            || _pendingOnCast.Value.GetEntitiesCount() > 0
            || _chainResolving.Value.GetEntitiesCount() > 0
            || _deathAnim.Value.GetEntitiesCount() > 0;

        // ── контекст хода (пересобирается на каждое действие — доска меняется) ─────────

        struct Ctx
        {
            public int Ai, Human;             // сущности игроков
            public int AiId, HumanId;         // PlayerId
            public int MyBoard, EnemyBoard;   // существ на доске (ИИ/человек)
            public int EnemyMaxAtk;           // самая сильная атака человека
            public int AiHand, HumanHand;     // карт в руках
            public int PersonalTurn;          // личный ход ИИ
            public int HumanHp;               // текущее HP человека (летал-чек)

            // Карта угроз СЛЕДУЮЩЕГО хода человека (см. BuildThreatMap):
            public Dictionary<(int, int, int), int> Threat;      // клетка → макс. атака, дотягивающаяся до неё
            public HashSet<(int, int, int)> EnemyReach;          // пустые клетки, куда враг может ВСТАТЬ
        }

        Ctx BuildCtx(int ai)
        {
            var c = new Ctx { Ai = ai, Human = -1, HumanId = -1 };
            c.AiId = _playerPool.Value.Get(ai).PlayerId;
            foreach (var e in _humanFilter.Value) { c.Human = e; c.HumanId = _playerPool.Value.Get(e).PlayerId; break; }
            c.HumanHp = c.Human >= 0 && _healthPool.Value.Has(c.Human) ? _healthPool.Value.Get(c.Human).Current : int.MaxValue;

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
            BuildThreatMap(ref c);
            return c;
        }

        /// <summary>
        /// Карта угроз СЛЕДУЮЩЕГО хода человека: BFS от каждого его существа по ПУСТЫМ клеткам с
        /// бюджетом Speed.Max − 1 (одна скорость уйдёт на сам удар; занятые клетки непроходимы — наши
        /// существа реально стены). Threat[клетка] = макс. атака среди врагов, способных ударить её;
        /// EnemyReach = пустые клетки, куда враг может встать (наш row 0 в этом сете = аватар под ударом,
        /// т.к. аватар бьётся только с row 0 его стороны).
        /// </summary>
        void BuildThreatMap(ref Ctx c)
        {
            c.Threat = new Dictionary<(int, int, int), int>();
            c.EnemyReach = new HashSet<(int, int, int)>();
            if (c.HumanId < 0) return;

            var cost = new Dictionary<(int, int, int), int>();
            var queue = new Queue<(int, int, int)>();

            foreach (var e in _boardCreatures.Value)
            {
                if (_ownerPool.Value.Get(e).OwnerId != c.HumanId) continue;
                int atk = _attackValuePool.Value.Has(e) ? _attackValuePool.Value.Get(e).Value : 0;
                if (atk <= 0) continue;   // не бьёт → не угроза
                int budget = (_speedPool.Value.Has(e) ? _speedPool.Value.Get(e).Max : 1) - 1;

                ref var p = ref _posPool.Value.Get(e);
                var start = (p.Row, p.Col, p.OwnerId);
                cost.Clear(); queue.Clear();
                cost[start] = 0;
                queue.Enqueue(start);

                while (queue.Count > 0)
                {
                    var (cr, cc, co) = queue.Dequeue();
                    int d = cost[(cr, cc, co)];
                    foreach (var (nr, nc, no) in Neighbours(cr, cc, co, c.AiId, c.HumanId))
                    {
                        // соседи КАЖДОЙ достижимой стойки (включая текущую позицию врага) — под ударом
                        var key = (nr, nc, no);
                        if (!c.Threat.TryGetValue(key, out int old) || atk > old) c.Threat[key] = atk;

                        if (d >= budget) continue;
                        if (cost.ContainsKey(key)) continue;
                        if (CreatureAt(nr, nc, no) >= 0) continue;   // занято → непроходимо
                        cost[key] = d + 1;
                        queue.Enqueue(key);
                    }
                }
                foreach (var cell in cost.Keys)
                    if (cell != start) c.EnemyReach.Add(cell);
            }
        }

        int ThreatAt(in Ctx c, int row, int col, int owner)
            => c.Threat != null && c.Threat.TryGetValue((row, col, owner), out int t) ? t : 0;

        /// <summary>Темперамент существа по его триггерам (авто, без разметки карт):
        /// Hider — турновый триггер (начало/конец хода): ценность капает, пока жив → прячем в тыл
        /// (только пока у человека есть существа — иначе воюет как все);
        /// Kamikaze — «при смерти» с ПОЛЕЗНЫМ эффектом (роль не Generic/Curse: служебный реверт аур и
        /// навешенные врагом проклятия не в счёт) → нарывается, лезет под удары;
        /// приоритет Hider &gt; Kamikaze (повторяемый движок ценнее одноразового хрипа).</summary>
        enum Temper { Normal, Hider, Kamikaze }

        Temper TemperOf(int card, in Ctx ctx)
        {
            if (ctx.EnemyBoard > 0 && HasTurnValueTrigger(card)) return Temper.Hider;
            if (HasUsefulDeathAbility(card)) return Temper.Kamikaze;
            return Temper.Normal;
        }

        /// <summary>Есть способность с турновым триггером (генерит ценность каждый ход, пока жив).
        /// Работает и для СВОИХ (прятать), и для существ человека (охотиться).</summary>
        bool HasTurnValueTrigger(int card)
        {
            if (!_abilityContainerPool.Value.Has(card)) return false;
            var abilities = _abilityContainerPool.Value.Get(card).AbilityEntities;
            if (abilities == null) return false;
            foreach (var a in abilities)
            {
                if (!_abilityTriggersPool.Value.Has(a)) continue;
                var triggers = _abilityTriggersPool.Value.Get(a).Triggers;
                if (triggers == null) continue;
                foreach (var t in triggers)
                    if (t != null && t.AiTurnCycle) return true;
            }
            return false;
        }

        /// <summary>Есть «при смерти» с полезным эффектом. Для своих — камикадзе; для существ человека —
        /// «вежливость» (не детонировать его хрип зря).</summary>
        bool HasUsefulDeathAbility(int card)
        {
            if (!_abilityContainerPool.Value.Has(card)) return false;
            var abilities = _abilityContainerPool.Value.Get(card).AbilityEntities;
            if (abilities == null) return false;
            foreach (var a in abilities)
            {
                if (!_abilityTriggersPool.Value.Has(a)) continue;
                var triggers = _abilityTriggersPool.Value.Get(a).Triggers;
                if (triggers == null) continue;
                foreach (var t in triggers)
                    if (t != null && t.AiDeathTrigger && HasUsefulDeathEffect(a)) return true;
            }
            return false;
        }

        // Эффекты OnDie-способности реально полезны владельцу? Generic — служебные (реверт tracked-баффов
        // аур); Curse — как правило навешенное ВРАГОМ «при смерти замешай проклятие себе» (Газовое
        // вздутие): за такое умирать не надо.
        bool HasUsefulDeathEffect(int abilityEntity)
        {
            if (!_abilityEffectsPool.Value.Has(abilityEntity)) return false;
            var effects = _abilityEffectsPool.Value.Get(abilityEntity).Effects;
            if (effects == null) return false;
            foreach (var effect in effects)
            {
                if (effect == null) continue;
                var role = effect.AiRole;
                if (role != AiEffectRole.Generic && role != AiEffectRole.Curse) return true;
            }
            return false;
        }

        // ── главный выбор действия ──────────────────────────────────────────────────────

        bool TryAct(int ai)
        {
            var ctx = BuildCtx(ai);

            // 1) Существо ждёт клетку → лучшая клетка фронта (блок угрожаемых колонок / свободная линия).
            // Идёт ПЕРВЫМ даже перед леталом — незакрытое размещение держит пайплайн.
            foreach (var card in _pendingCell.Value)
            {
                if (!_ownerPool.Value.Has(card) || _ownerPool.Value.Get(card).OwnerId != ctx.AiId) continue;
                int col = BestPlacementCol(ctx, TemperOf(card, ctx));
                int clickEntity = _world.Value.NewEntity();
                ref var click = ref _clickPool.Value.Add(clickEntity);
                click.Row = col >= 0 ? FrontRow : -1;   // нет клетки → «клик мимо» = отмена с рефандом
                click.Col = col >= 0 ? col : 0;
                click.OwnerId = ctx.AiId;
                Debug.Log($"[AI] размещаю существо card={card} col={col}");
                return true;
            }

            // 1.5) ЛЕТАЛ — раньше карт и разменов, и не только «уже стоит на фронт-ряду»: считаем, сколько
            // лица снимут ВСЕ существа, способные в ЭТОТ ход дойти до row 0 человека и ударить (BFS,
            // шаги + удар ≤ Remaining; клетки row 0 раздаются без повторов). Сумма ≥ HP → турбо-режим:
            // стоящие бьют, идущие шагают по путям — никаких разменов по дороге.
            if (TryLethalRush(ctx)) return true;

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
                if (!_forceRandomPool.Value.Has(bestCard)) _forceRandomPool.Value.Add(bestCard);   // Selected → авто-выбор
                // Умный таргет вместо случайного: критерий по доминирующей роли эффектов карты.
                if (!_aiPrefPool.Value.Has(bestCard)) _aiPrefPool.Value.Add(bestCard);
                _aiPrefPool.Value.Get(bestCard).Mode = PreferenceFor(bestCard);
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
                if (AvatarExposed(ctx)) s += 6;   // враг дотягивается до нашего row 0 — срочно нужно тело-блок
                // Enabler-комбо: в руке бафф без носителя → сначала тело, бафф подорожает сам (без своих он -50).
                if (ctx.MyBoard == 0 && HandHasRole(ctx, AiEffectRole.BuffAlly)) s += 3;
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
                    // Рампа ПЕРВОЙ в ходу, если открывает недоступную сейчас карту (ресурс → дорогая угроза).
                    if (HandHasUnaffordable(ctx)) return 12;
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

        // В руке ИИ есть карта, которую сейчас НЕ потянуть по ресурсам — кандидат на «сначала рампа».
        bool HandHasUnaffordable(in Ctx ctx)
        {
            foreach (var card in _handCards.Value)
            {
                if (_ownerPool.Value.Get(card).OwnerId != ctx.AiId) continue;
                if (_commanderCdPool.Value.Has(card)) continue;
                if (!Affordable(card, ctx.Ai)) return true;
            }
            return false;
        }

        // В руке ИИ есть карта с эффектом данной роли (enabler-пары: «призыв ← бафф в руке» и т.п.).
        bool HandHasRole(in Ctx ctx, AiEffectRole role)
        {
            foreach (var card in _handCards.Value)
            {
                if (_ownerPool.Value.Get(card).OwnerId != ctx.AiId) continue;
                if (!_abilityContainerPool.Value.Has(card)) continue;
                var abilities = _abilityContainerPool.Value.Get(card).AbilityEntities;
                if (abilities == null) continue;
                foreach (var a in abilities)
                {
                    if (!_abilityEffectsPool.Value.Has(a)) continue;
                    var effects = _abilityEffectsPool.Value.Get(a).Effects;
                    if (effects == null) continue;
                    foreach (var effect in effects)
                        if (effect != null && effect.AiRole == role) return true;
                }
            }
            return false;
        }

        // Критерий авто-таргета по доминирующей роли эффектов карты: removal/дебафф/проклятие → самая
        // опасная цель; урон → добивание (мин. HP); лечение → самое раненое; бафф → лучший носитель.
        // КОГО можно выбирать (враг/союзник/зона) уже задано фильтрами карты — тут только «какого именно».
        AiTargetPreference PreferenceFor(int card)
        {
            bool removal = false, damage = false, heal = false, buff = false;
            if (_abilityContainerPool.Value.Has(card))
            {
                var abilities = _abilityContainerPool.Value.Get(card).AbilityEntities;
                if (abilities != null)
                    foreach (var a in abilities)
                    {
                        if (!_abilityEffectsPool.Value.Has(a)) continue;
                        var effects = _abilityEffectsPool.Value.Get(a).Effects;
                        if (effects == null) continue;
                        foreach (var effect in effects)
                        {
                            if (effect == null) continue;
                            switch (effect.AiRole)
                            {
                                case AiEffectRole.Removal:
                                case AiEffectRole.DebuffEnemy:
                                case AiEffectRole.Curse:    removal = true; break;
                                case AiEffectRole.Damage:   damage = true; break;
                                case AiEffectRole.Heal:     heal = true; break;
                                case AiEffectRole.BuffAlly: buff = true; break;
                            }
                        }
                    }
            }
            if (removal) return AiTargetPreference.HighestAttack;
            if (damage)  return AiTargetPreference.LowestHealth;
            if (heal)    return AiTargetPreference.MostDamaged;
            if (buff)    return AiTargetPreference.HighestAttack;
            return AiTargetPreference.None;
        }

        // ── размещение: перекрыть подход к аватару / спрятать генератора ────────────────

        // Свой row 0 — единственный ряд призыва И единственный ряд, с которого бьётся наш аватар:
        // занять клетку, куда враг реально дотягивается следующим ходом (EnemyReach), = физически
        // закрыть проход телом. Hider наоборот — в безопасную колонну; Kamikaze — охотнее под удар.
        int BestPlacementCol(in Ctx ctx, Temper temper)
        {
            int bestCol = -1, bestScore = int.MinValue;
            foreach (var col in ColOrder)
            {
                if (CreatureAt(FrontRow, col, ctx.AiId) >= 0) continue;   // занято

                int score;
                bool mineInCol = ColumnHasCreatureOf(col, ctx.AiId);
                bool breach = ctx.EnemyReach != null && ctx.EnemyReach.Contains((FrontRow, col, ctx.AiId));

                if (temper == Temper.Hider)
                {
                    // Генератора прячем: клетка вне досягаемости чужих атак; на пути прорыва не ставим.
                    score = ThreatAt(ctx, FrontRow, col, ctx.AiId) == 0 ? 10 : 2;
                    if (breach) score -= 4;
                }
                else
                {
                    int enemyDist = NearestEnemyDistInCol(col, ctx);       // DistToAiAvatar ближайшего врага колонны, 4 = нет
                    if (breach)              score = 14;   // враг встанет сюда след. ходом → закрываем клетку атаки аватара
                    else if (enemyDist <= 1) score = 12;   // враг уже на нашей половине колонны
                    else if (enemyDist == 2) score = 8;    // враг на линии фронта
                    else if (enemyDist == 3) score = 5;    // враг в тылу своей колонны
                    else                     score = 4;    // свободная линия — давим
                    if (mineInCol) score -= 3;             // колонна уже контестится нами
                    // Хрип хочет под удар: клетка простреливается → детонация ближе.
                    if (temper == Temper.Kamikaze && ThreatAt(ctx, FrontRow, col, ctx.AiId) > 0) score += 3;
                }

                if (score > bestScore) { bestScore = score; bestCol = col; }
            }
            return bestCol;
        }

        int NearestEnemyDistInCol(int col, in Ctx ctx)
        {
            int best = 4;
            foreach (var e in _boardCreatures.Value)
            {
                if (_ownerPool.Value.Get(e).OwnerId != ctx.HumanId) continue;
                ref var p = ref _posPool.Value.Get(e);
                if (p.Col != col) continue;
                int d = DistToAiAvatar(p.Row, p.OwnerId, ctx);
                if (d < best) best = d;
            }
            return best;
        }

        /// <summary>Враг может встать на СВОБОДНУЮ клетку нашего row 0 следующим ходом → аватар под ударом.</summary>
        bool AvatarExposed(in Ctx ctx)
        {
            if (ctx.EnemyReach == null) return false;
            for (int col = 0; col < Cols; col++)
                if (CreatureAt(FrontRow, col, ctx.AiId) < 0 && ctx.EnemyReach.Contains((FrontRow, col, ctx.AiId)))
                    return true;
            return false;
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

        // ЛЕТАЛ-МАРШ: суммарное лицо ЭТОГО хода = стоящие на row 0 человека (атака доступна) + все, кто
        // успевает дойти до СВОБОДНОЙ клетки row 0 и ударить (шаги + 1 ≤ Remaining). Клетки раздаются
        // жадно: сильнейшие первыми, каждому ближайшая свободная колонна. Сумма ≥ HP → исполняем по одному
        // действию за тик (план пересчитывается каждый тик — сходится, HP только падает, пути пересчитываются).
        // _attempted НЕ смотрим: летал — форсированный режим поверх обычных эвристик.
        bool TryLethalRush(in Ctx ctx)
        {
            if (ctx.Human < 0 || ctx.HumanHp == int.MaxValue) return false;

            int total = 0;
            var standing = new List<int>();
            var walkers = new List<(int creature, int atk, (int, int, int) start,
                                    Dictionary<(int, int, int), int> cost,
                                    Dictionary<(int, int, int), (int, int, int)> parent)>();
            var takenCols = new HashSet<int>();   // клетки row 0 человека, закреплённые за бойцами

            foreach (var creature in _boardCreatures.Value)
            {
                if (_ownerPool.Value.Get(creature).OwnerId != ctx.AiId) continue;
                if (_movingPool.Value.Has(creature)) continue;
                if (AttacksUsed(creature) >= MaxAttacksFor(creature)) continue;
                int remaining = _speedPool.Value.Get(creature).Remaining;
                if (remaining <= 0) continue;
                int atk = _attackValuePool.Value.Has(creature) ? _attackValuePool.Value.Get(creature).Value : 0;
                if (atk <= 0) continue;

                ref var pos = ref _posPool.Value.Get(creature);
                if (pos.OwnerId == ctx.HumanId && pos.Row == FrontRow)
                {
                    standing.Add(creature);
                    takenCols.Add(pos.Col);
                    total += atk;
                    continue;
                }
                var (cost, parent) = ReachFrom(pos.Row, pos.Col, pos.OwnerId, remaining - 1, ctx);
                walkers.Add((creature, atk, (pos.Row, pos.Col, pos.OwnerId), cost, parent));
            }

            // Жадная раздача свободных колонн row 0: сильнейшие первыми, каждому — ближайшая.
            walkers.Sort((a, b) => b.atk.CompareTo(a.atk));
            var assigned = new List<(int creature, List<(int Row, int Col, int Owner)> path)>();
            foreach (var w in walkers)
            {
                int bestCol = -1, bestCost = int.MaxValue;
                for (int col = 0; col < Cols; col++)
                {
                    if (takenCols.Contains(col)) continue;
                    if (w.cost.TryGetValue((FrontRow, col, ctx.HumanId), out int cc) && cc < bestCost)
                    { bestCost = cc; bestCol = col; }
                }
                if (bestCol < 0) continue;
                takenCols.Add(bestCol);
                total += w.atk;
                assigned.Add((w.creature, PathBack(w.parent, w.start, (FrontRow, bestCol, ctx.HumanId))));
            }

            if (total < ctx.HumanHp) return false;

            // Исполнение: сперва стоящие бьют (урон падает уже сейчас), потом идущие шагают.
            if (standing.Count > 0)
            {
                int c = standing[0];
                MarkAttacked(c);
                if (!_attackPool.Value.Has(c)) _attackPool.Value.Add(c).TargetEntity = ctx.Human;
                Debug.Log($"[AI] ЛЕТАЛ: существо {c} бьёт аватара (суммарно {total} ≥ hp={ctx.HumanHp})");
                return true;
            }
            if (assigned.Count > 0)
            {
                assigned.Sort((a, b) => a.path.Count.CompareTo(b.path.Count));   // ближайший к финишу — первым
                var (creature, path) = assigned[0];
                var step = path[0];
                if (!_movePool.Value.Has(creature))
                {
                    ref var mv = ref _movePool.Value.Add(creature);
                    mv.ToRow = step.Row; mv.ToCol = step.Col; mv.ToOwnerId = step.Owner;
                }
                Debug.Log($"[AI] ЛЕТАЛ-МАРШ: существо {creature} шаг к ({step.Row},{step.Col}) (суммарно {total} ≥ hp={ctx.HumanHp})");
                return true;
            }
            return false;
        }

        // BFS по ПУСТЫМ клеткам от позиции существа (бюджет шагов) + parent для восстановления пути.
        (Dictionary<(int, int, int), int> cost, Dictionary<(int, int, int), (int, int, int)> parent) ReachFrom(
            int row, int col, int cellOwner, int maxSteps, in Ctx ctx)
        {
            var cost = new Dictionary<(int, int, int), int>();
            var parent = new Dictionary<(int, int, int), (int, int, int)>();
            var start = (row, col, cellOwner);
            cost[start] = 0;
            if (maxSteps <= 0) return (cost, parent);

            var queue = new Queue<(int, int, int)>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var (cr, cc, co) = queue.Dequeue();
                int d = cost[(cr, cc, co)];
                if (d >= maxSteps) continue;
                foreach (var next in Neighbours(cr, cc, co, ctx.AiId, ctx.HumanId))
                {
                    if (cost.ContainsKey(next)) continue;
                    if (CreatureAt(next.row, next.col, next.owner) >= 0) continue;   // занято → непроходимо
                    cost[next] = d + 1;
                    parent[next] = (cr, cc, co);
                    queue.Enqueue(next);
                }
            }
            return (cost, parent);
        }

        static List<(int Row, int Col, int Owner)> PathBack(
            Dictionary<(int, int, int), (int, int, int)> parent, (int, int, int) start, (int, int, int) goal)
        {
            var path = new List<(int Row, int Col, int Owner)>();
            var cur = goal;
            while (cur != start) { path.Add(cur); cur = parent[cur]; }
            path.Reverse();
            return path;
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
                bool canAttack = AttacksUsed(creature) < MaxAttacksFor(creature);
                int myAtk = _attackValuePool.Value.Has(creature) ? _attackValuePool.Value.Get(creature).Value : 0;
                int myHp  = _healthPool.Value.Has(creature) ? _healthPool.Value.Get(creature).Current : 1;
                var temper = TemperOf(creature, ctx);
                bool hider = temper == Temper.Hider;         // «ресурсный» — прячется (пока у человека есть существа)
                bool kamikaze = temper == Temper.Kamikaze;   // «при смерти» — нарывается
                int threatHere = ThreatAt(ctx, pos.Row, pos.Col, pos.OwnerId);

                // «Защитник»: рядом (без движения, текущая позиция) вражеский Защитник → атаковать можно
                // только его. Проверяется здесь один раз на существо (позиция не меняется до следующего тика).
                bool tauntBlocks = HasAdjacentEnemyTaunt(pos.Row, pos.Col, pos.OwnerId, ctx);

                // Атака аватара: только с фронт-ряда человека (его row 0). Летал ловится раньше
                // (TryLethalRush), здесь — обычный урон в лицо; чем ниже HP человека, тем ценнее.
                if (canAttack && !tauntBlocks && pos.OwnerId == ctx.HumanId && pos.Row == FrontRow && ctx.Human >= 0
                    && !_attempted.Contains(AttemptKey(creature, Kind.Attack, ctx.Human)))
                {
                    int faceScore = 22 + (ctx.HumanHp <= myAtk * 2 ? 6 : 0);   // «в двух ударах от летала» — дожимаем
                    if (faceScore > bestScore)
                    { bestScore = faceScore; bestCreature = creature; bestKind = (int)Kind.Attack; bestTarget = ctx.Human; }
                }

                foreach (var (nr, nc, no) in Neighbours(pos.Row, pos.Col, pos.OwnerId, ctx.AiId, ctx.HumanId))
                {
                    int occupant = CreatureAt(nr, nc, no);

                    if (occupant >= 0)
                    {
                        // Враг на соседней клетке → оценка размена. «Скрытый» не выбирается вовсе; «Защитник»
                        // рядом ограничивает выбор ИМ (tauntBlocks && occupant не Защитник → пропуск).
                        if (_ownerPool.Value.Get(occupant).OwnerId != ctx.AiId && canAttack
                            && !_stealthPool.Value.Has(occupant)
                            && (!tauntBlocks || _tauntPool.Value.Has(occupant))
                            && !_attempted.Contains(AttemptKey(creature, Kind.Attack, occupant)))
                        {
                            int tHp  = _healthPool.Value.Has(occupant) ? _healthPool.Value.Get(occupant).Current : 1;
                            int tAtk = _attackValuePool.Value.Has(occupant) ? _attackValuePool.Value.Get(occupant).Value : 0;

                            int s;
                            if (myAtk >= tHp)            s = 26 + tAtk * 2;       // килл: чем опаснее цель, тем ценнее
                            else if (tAtk >= 3 && myAtk + AlliedAdjacentAttack(occupant, creature, ctx) >= tHp)
                                                          s = 20 + tAtk;           // фокус-файр: опасную цель добиваем вскладчину
                            else if (tAtk >= myHp)       s = kamikaze ? 14 : 3;   // добьют в ответ: хрипу того и надо
                            else                          s = 8;                   // просто обмен уроном
                            if (hider && myAtk < tHp) s -= 4;                     // генератора без килла не размениваем
                            if (HasTurnValueTrigger(occupant))   s += 6;          // охота: генератор человека — приоритетная цель
                            if (HasUsefulDeathAbility(occupant)) s -= 5;          // вежливость: его хрип зря не детонируем
                            if (s > bestScore)
                            { bestScore = s; bestCreature = creature; bestKind = (int)Kind.Attack; bestTarget = occupant; }
                        }
                        continue;   // клетка занята — шаг невозможен
                    }

                    // «Защитник» рядом (без движения) — существо не может уйти ни в какую сторону, только
                    // атаковать его (или ничего): «нет других вариантов действия».
                    if (tauntBlocks) continue;

                    // Свободная клетка → шаг, но ТОЛЬКО осмысленный (боковые «прогулки» ради прогулки
                    // давали блуждание туда-сюда — теперь база вбок/назад = 0/-10, шаг берётся, если у него
                    // есть ЦЕЛЬ: прогресс, обход блока, подход на добивание, перекрытие прохода, безопасность).
                    long move = AttemptKey(creature, Kind.Move, no * 100 + nr * 10 + nc);
                    if (_attempted.Contains(move)) continue;

                    int threatThere = ThreatAt(ctx, nr, nc, no);
                    int s2;

                    if (hider)
                    {
                        // «Ресурсное» существо: вперёд не идёт — уходит из-под ударов в безопасный тыл.
                        int retreat = DistToAiAvatar(pos.Row, pos.OwnerId, ctx) - DistToAiAvatar(nr, no, ctx);   // >0 = к своему тылу
                        s2 = 0;
                        if (threatHere > 0 && threatThere == 0) s2 += 14;   // выход из-под удара
                        else if (threatThere < threatHere)      s2 += 6;    // хотя бы под более слабый удар
                        if (threatThere > 0 && threatHere == 0) s2 -= 15;   // из безопасности под удар не лезем
                        if (threatHere > 0 && retreat > 0)      s2 += 4;    // пока опасно — тянемся к тылу
                    }
                    else
                    {
                        int progress = DistToAvatar(pos.Row, pos.OwnerId, ctx) - DistToAvatar(nr, no, ctx);
                        s2 = progress * 10;                           // вперёд 10, вбок 0, назад -10

                        // Смена колонки, когда вперёд перекрыто СВОИМ — единственная «полезная боковушка».
                        if (progress == 0 && ForwardBlockedByAlly(pos, ctx)) s2 += 8;

                        // Подход на добивание/окружение: из целевой клетки виден враг, которого убиваем следующим
                        // действием (атака ещё доступна и после шага останется скорость на удар).
                        if (canAttack && _speedPool.Value.Get(creature).Remaining >= 2
                            && AdjacentKillableEnemy(nr, nc, no, myAtk, ctx))
                            s2 += 12;

                        // Перекрыть проход: клетка на НАШЕЙ половине, в колонне прорывающегося человека, между
                        // ним и нашим аватаром — живая стена.
                        if (BlocksEnemyPath(nr, nc, no, ctx)) s2 += 9;

                        // Не бросать стену: уход с клетки, перекрывающей прорыв, открывает проход к аватару
                        // (зеркало +9 за вход — иначе «подход на добивание» уводил стену вбок).
                        if (BlocksEnemyPath(pos.Row, pos.Col, pos.OwnerId, ctx) && !BlocksEnemyPath(nr, nc, no, ctx))
                            s2 -= 9;

                        if (kamikaze)
                        {
                            // «При смерти» нарывается: безопасность игнорирует, наоборот лезет туда, где
                            // его добьют (враг вынужден тратить атаку → хрип детонирует). Строгие «>» —
                            // защита от пинг-понга между равноопасными клетками.
                            if (threatThere >= myHp && threatHere < myHp) s2 += 10;   // встать под добивание
                            else if (threatThere > threatHere)            s2 += 3;    // хотя бы под удар посильнее
                        }
                        else
                        {
                            // Безопасность (карта угроз следующего хода человека):
                            // 1) там нас убивают, тут — нет → без нужды под смерть не подставляемся;
                            if (threatThere >= myHp && threatHere < myHp) s2 -= 8;
                            // 2) тут убивают, там — нет, а размена нет → спасаемся (любое направление: вбок 0+16,
                            //    назад -10+16 — вместо стояния под ударом);
                            if (threatHere >= myHp && threatThere < myHp
                                && !(canAttack && AdjacentKillableEnemy(pos.Row, pos.Col, pos.OwnerId, myAtk, ctx)))
                                s2 += 16;
                            // 3) hit-and-run: атака уже потрачена, скорость осталась — бесплатно сместиться
                            //    из-под удара на безопасную клетку (ударил и отошёл).
                            if (!canAttack && threatHere > 0 && threatThere == 0) s2 += 6;
                            // 4) тай-брейк: при прочих равных предпочитаем клетку без угрозы.
                            if (threatThere > 0) s2 -= 1;
                        }
                    }

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

        /// <summary>Дистанция до аватара ИИ (зеркало DistToAvatar): наш row0=0, наш row1=1, чужой row1=2, чужой row0=3.</summary>
        int DistToAiAvatar(int row, int owner, in Ctx ctx)
            => owner == ctx.AiId ? (row == FrontRow ? 0 : 1) : (row == BackRow ? 2 : 3);

        // Суммарная атака ДРУГИХ моих существ (кроме except), смежных к цели, у которых атака ещё
        // доступна — фокус-файр: вместе добиваем то, что не убить одному. Порядок доигрывается сам:
        // после первого удара HP цели падает, следующий тик видит соло-килл (26+) и завершает.
        int AlliedAdjacentAttack(int target, int except, in Ctx ctx)
        {
            ref var tp = ref _posPool.Value.Get(target);
            int sum = 0;
            foreach (var (nr, nc, no) in Neighbours(tp.Row, tp.Col, tp.OwnerId, ctx.AiId, ctx.HumanId))
            {
                int ally = CreatureAt(nr, nc, no);
                if (ally < 0 || ally == except) continue;
                if (_ownerPool.Value.Get(ally).OwnerId != ctx.AiId) continue;
                if (AttacksUsed(ally) >= MaxAttacksFor(ally)) continue;
                if (_speedPool.Value.Get(ally).Remaining <= 0) continue;
                sum += _attackValuePool.Value.Has(ally) ? _attackValuePool.Value.Get(ally).Value : 0;
            }
            return sum;
        }

        // Из клетки (r,c,o) виден соседний враг, которого убиваем текущей атакой — подход на добивание.
        bool AdjacentKillableEnemy(int r, int c, int o, int myAtk, in Ctx ctx)
        {
            foreach (var (nr, nc, no) in Neighbours(r, c, o, ctx.AiId, ctx.HumanId))
            {
                int e = CreatureAt(nr, nc, no);
                if (e < 0 || _ownerPool.Value.Get(e).OwnerId == ctx.AiId) continue;
                if (_stealthPool.Value.Has(e)) continue;   // «Скрытый» не выбирается как цель
                int hp = _healthPool.Value.Has(e) ? _healthPool.Value.Get(e).Current : 1;
                if (myAtk >= hp) return true;
            }
            return false;
        }

        // «Защитник»: есть ли вражеский Защитник на клетке, СМЕЖНОЙ с (row,col,owner) — без движения.
        bool HasAdjacentEnemyTaunt(int row, int col, int owner, in Ctx ctx)
        {
            foreach (var (nr, nc, no) in Neighbours(row, col, owner, ctx.AiId, ctx.HumanId))
            {
                int e = CreatureAt(nr, nc, no);
                if (e >= 0 && _ownerPool.Value.Get(e).OwnerId != ctx.AiId && _tauntPool.Value.Has(e)) return true;
            }
            return false;
        }

        // Клетка перекрывает колонну прорывающегося человека: она на НАШЕЙ половине и в этой колонне есть
        // существо человека БЛИЖЕ к нашему аватару, чем было бы без стены (мы встаём между ним и аватаром).
        bool BlocksEnemyPath(int r, int c, int o, in Ctx ctx)
        {
            if (o != ctx.AiId) return false;   // стена имеет смысл только на своей половине
            int cellDist = DistToAiAvatar(r, o, ctx);
            foreach (var e in _boardCreatures.Value)
            {
                if (_ownerPool.Value.Get(e).OwnerId != ctx.HumanId) continue;
                ref var p = ref _posPool.Value.Get(e);
                if (p.Col != c) continue;
                int enemyDist = DistToAiAvatar(p.Row, p.OwnerId, ctx);
                if (enemyDist <= 2 && cellDist < enemyDist) return true;   // враг уже прорывается, мы — между
            }
            return false;
        }

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

        // «Двойной удар» поднимает лимит атак за ход с 1 до 2 — как в RunSelectCellSystem/RunPathMoveSystem.
        int MaxAttacksFor(int e) => MaxAttacksPerTurn + (_doubleAttackPool.Value.Has(e) ? 1 : 0);

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
