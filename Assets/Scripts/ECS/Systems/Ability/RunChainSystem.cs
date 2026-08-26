using System.Collections.Generic;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Mono;
using Game.Core.Service;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using UnityEngine;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// Контроллер составных (цепочечных) способностей. Ведёт ChainStateComponent по стадиям из
    /// AbilityChainComponent: применяет текущую стадию (свой таргетинг + эффекты), ждёт ОСЕДАНИЯ мира
    /// (урон применён, смерти обработаны), считает погибших среди целей стадии → накапливает Killed
    /// (контекст для CountSource=ChainKilled), шагает дальше. По завершении снимает state.
    ///
    /// Запускается только у активного клиента (ChainStateComponent ставит RunCheckAbilityRulesSystem
    /// по AbilityCastEvent, а тот гейтит пассив через AbilityFire). СИНК стадий (StepIndex) и
    /// недетерминированная генерация РЕАЛИЗОВАНЫ (см. ReplayActionSystem.ApplyChainStage — пассив
    /// применяет эффекты стадии к целям ИЗ СНАПШОТА, не пере-резолвит; недетерм. генерация грузится из
    /// присланных идентичностей через GeneratedCardChannel.LoadReplay). VFX стадии на пассиве — та же
    /// точка (RunChainSystem там не крутится → ApplyChainStage эмитит косметику САМ, см. её докстринг).
    ///
    /// СНАРЯД (2026-08-11): у стадии со Vfx.Kind=Projectile эффекты НЕ применяются сразу — публикуем
    /// ProjectileVfxEvent на каждую цель, вешаем ChainProjectilePendingComponent (СВОЙ гейт, не
    /// AbilityCastPendingComponent — тот принадлежит RunResolveAbilityQueueSystem и слушает
    /// VfxArrivedEvent ГЛОБАЛЬНО; общий токен с ability-сущностью цепочки заставил бы обе системы
    /// среагировать на один и тот же прилёт) и ждём VfxArrivedEvent. На прилёте (или по таймауту —
    /// анти-софтлок) применяем эффекты по уже выбранным (не перевыбранным!) целям стадии.
    /// </summary>
    public sealed class RunChainSystem : IEcsInitSystem, IEcsRunSystem, IEcsDestroySystem
    {
        readonly EcsCustomInject<BoardView> _boardView = default;
        readonly EcsPoolInject<ViewRefComponent> _viewPool = default;

        const float ProjectileTimeout = 4f;   // сек до форс-резолва стадии, если прилёт не пришёл
        const float AbilityAnimTimeout = 4f;  // сек до форс-резолва, если клип не прислал CastEvent/FinishEvent

        readonly Queue<int> _arrived = new Queue<int>();          // токены приземлившихся снарядов (ability-сущности)
        readonly Queue<int> _castPointReached = new Queue<int>(); // CastEvent анимации кастера стадии
        readonly Queue<int> _animFinished = new Queue<int>();     // FinishEvent анимации кастера стадии
        bool _subscribed;

        public void Init(IEcsSystems systems) => Subscribe();
        public void Destroy(IEcsSystems systems)
        {
            if (!_subscribed) return;
            GameEventBus.Unsubscribe<VfxArrivedEvent>(OnArrived);
            _subscribed = false;
        }
        void Subscribe()
        {
            if (_subscribed) return;
            _subscribed = true;
            GameEventBus.Subscribe<VfxArrivedEvent>(OnArrived);
        }
        void OnArrived(VfxArrivedEvent e) => _arrived.Enqueue(e.Token);

        public void Run(IEcsSystems systems)
        {
            var world = systems.GetWorld();
            var statePool = world.GetPool<ChainStateComponent>();
            var chainPool = world.GetPool<AbilityChainComponent>();
            var ownerPool = world.GetPool<AbilityOwnerComponent>();
            var projectilePool = world.GetPool<ChainProjectilePendingComponent>();

            // Прилёт снаряда стадии → форсируем Deadline в прошлое (НЕ удаляем компонент здесь!). Основной
            // цикл ниже сам снимет pending и применит эффекты — РОВНО в его ветке «projectilePool.Has(entity)».
            // Баг 2026-08-11 (бесконечный спавн снарядов): удаление pending ЗДЕСЬ означало, что к моменту
            // цикла ниже projectilePool.Has(entity) уже false, а state.Applied всё ещё false → ветка «стадия
            // ещё не начата» перевыбирала цели и запускала НОВЫЙ снаряд заново, бесконечно (эффект так и не
            // применялся). Держим компонент живым до тех пор, пока его явно не снимет ветка обработки.
            var vfxStepsPendingPool = world.GetPool<VfxStepsPendingComponent>();
            while (_arrived.Count > 0)
            {
                int token = _arrived.Dequeue();
                if (token < 0) continue;
                if (projectilePool.Has(token))
                {
                    ref var p = ref projectilePool.Get(token);
                    p.Deadline = 0f;
                }
                // VFX-таймлайн стадии (VfxSteps) — тот же Token=entity может прилетать НЕСКОЛЬКО раз (по
                // одному на Projectile-шаг), считаем счётчиком (см. VfxEmitUtil.TryLaunchDueSteps).
                if (vfxStepsPendingPool.Has(token)) vfxStepsPendingPool.Get(token).PendingArrivals--;
            }

            // Анимация кастера стадии (opt-in, VfxSpec.PlayCasterAnimation): CastEvent → применить эффекты
            // стадии/запустить снаряд (РОВНО один раз, CastApplied-гард), FinishEvent → снять гейт. Тот же
            // принцип, что у RunResolveAbilityQueueSystem, только гейт свой (ChainCastAnimPendingComponent) —
            // блокирует продвижение СТАДИЙ этой цепочки (см. WorldSettled), а не глобальную очередь.
            var castAnimPool = world.GetPool<ChainCastAnimPendingComponent>();
            while (_castPointReached.Count > 0) CompleteChainCastPoint(world, _castPointReached.Dequeue());
            while (_animFinished.Count > 0)     CompleteChainAnimGate(world, _animFinished.Dequeue());
            foreach (var e in world.Filter<ChainCastAnimPendingComponent>().End())
            {
                if (castAnimPool.Get(e).Deadline <= Time.time) { CompleteChainAnimGate(world, e); break; }
            }

            var filter = world.Filter<ChainStateComponent>().Inc<AbilityChainComponent>().Inc<AbilityOwnerComponent>().End();

            var buffer = new List<int>();
            foreach (var e in filter) buffer.Add(e);   // буфер: меняем/снимаем компоненты по ходу

            foreach (var entity in buffer)
            {
                ref var state = ref statePool.Get(entity);
                var stages = chainPool.Get(entity).Stages;

                if (stages == null || state.Current >= stages.Length)
                {
                    statePool.Del(entity);
                    if (projectilePool.Has(entity)) projectilePool.Del(entity);   // цепочку прервали — снаряд не долетит
                    if (vfxStepsPendingPool.Has(entity)) vfxStepsPendingPool.Del(entity);   // ...или таймлайн шагов
                    continue;
                }

                if (!state.Applied)
                {
                    if (castAnimPool.Has(entity)) continue;   // анимация кастера стадии ещё играет (ждём CastEvent/FinishEvent)

                    // VFX-таймлайн стадии (VfxSteps) уже запущен — тикаем шаги и ждём, пока все не долетят
                    // (аналог ветки projectilePool ниже, но для НЕСКОЛЬКИХ параллельных/растянутых шагов).
                    if (vfxStepsPendingPool.Has(entity))
                    {
                        ref var vp = ref vfxStepsPendingPool.Get(entity);
                        var vsteps = world.GetPool<AbilityVfxStepsComponent>().Get(entity).Steps;
                        int vOwnerPlayer = ownerPool.Get(entity).PlayerEntity;
                        VfxEmitUtil.TryLaunchDueSteps(world, _boardView.Value, entity, ownerPool.Get(entity).CardEntity,
                                                       vOwnerPlayer, state.LastTargets ?? System.Array.Empty<int>(), vsteps, ref vp);
                        if (!VfxEmitUtil.AllStepsLaunched(vp) || vp.PendingArrivals > 0) continue;   // не всё долетело
                        vfxStepsPendingPool.Del(entity);
                        GameEventBus.Publish(new InputRestoredEvent());
                        FinishStage(world, entity, ref state, stages, ownerPool);
                        continue;
                    }

                    if (projectilePool.Has(entity))
                    {
                        if (projectilePool.Get(entity).Deadline > Time.time) continue;   // снаряд ещё в полёте
                        projectilePool.Del(entity);   // анти-софтлок — форсим по уже выбранным LastTargets
                        FinishStage(world, entity, ref state, stages, ownerPool);
                        continue;
                    }

                    ref var owner = ref ownerPool.Get(entity);
                    var stage = stages[state.Current];

                    int[] targets = ResolveTargets(world, stage, owner.CardEntity, owner.PlayerEntity);
                    state.LastTargets = targets;   // ДО возможного ожидания снаряда/анимации — резолв на прилёте/CastEvent бьёт ТЕ ЖЕ цели

                    // FixedInterval (см. ChainStage.AdvanceMode) — НЕ ждём прилёта: косметика улетает
                    // fire-and-forget, эффект применяется сразу же. Без этого каждая активация RepeatAbility
                    // ждёт ПОЛНОГО времени полёта своего снаряда, прежде чем следующая вообще начнёт
                    // выбирать цель — GapSecondsOverride тогда сокращает только маленькую паузу ПОСЛЕ
                    // прилёта, а не доминирующее время полёта (баг 2026-08-24 — «Расстрелять» на 0.01 всё
                    // равно бил одиночными выстрелами, не очередью внахлёст).
                    if (targets.Length > 0 && stage.Advance == ChainStage.AdvanceMode.FixedInterval)
                    {
                        var stepsFI = world.GetPool<AbilityVfxStepsComponent>();
                        if (stepsFI.Has(entity) && stepsFI.Get(entity).Steps is { Count: > 0 } stepsList && _boardView.Value != null)
                            FireAndForgetVfxSteps(world, entity, owner.CardEntity, owner.PlayerEntity, targets, stepsList);
                        else if (world.GetPool<AbilityVfxComponent>().Has(entity) && _boardView.Value != null)
                            FireAndForgetLegacyVfx(world, owner.CardEntity, targets, world.GetPool<AbilityVfxComponent>().Get(entity).Spec);

                        FinishStage(world, entity, ref state, stages, ownerPool);
                        continue;
                    }

                    // ПРИОРИТЕТ: VFX-таймлайн (VfxSteps) — та же логика, что в RunResolveAbilityQueueSystem.
                    // ResolveOrLaunch: если способность собрана через конструктор шагов, легаси Vfx-путь ниже
                    // для неё не применяется (Ability.Init кладёт только ОДИН из двух компонентов).
                    var vfxStepsPool = world.GetPool<AbilityVfxStepsComponent>();
                    if (targets.Length > 0 && vfxStepsPool.Has(entity) && _boardView.Value != null)
                    {
                        var stepsList = vfxStepsPool.Get(entity).Steps;
                        if (stepsList != null && stepsList.Count > 0)
                        {
                            LaunchStageVfxSteps(world, entity, owner.CardEntity, targets, stepsList);
                            continue;
                        }
                    }

                    var vfxPool = world.GetPool<AbilityVfxComponent>();
                    var spec = vfxPool.Has(entity) ? vfxPool.Get(entity).Spec : null;
                    // ВРЕМЕННО (баг: снаряд цепочки не летит) — видим, почему ветка снаряда не сработала.
                    UnityEngine.Debug.Log($"[ChainVfx] entity={entity} step={state.Current} targets={targets.Length} hasVfxComp={vfxPool.Has(entity)} spec={(spec == null ? "NULL" : "ok")} kind={spec?.Kind} prefab={(spec?.Prefab != null ? spec.Prefab.name : "NULL")} boardView={(_boardView.Value != null ? "ok" : "NULL")}");

                    // Анимация кастера (opt-in, PlayCasterAnimation=true) — ДО решения снаряд/мгновенно, как
                    // в RunResolveAbilityQueueSystem: без неё эффекты стадии применяются молча без анимации
                    // на кастере (баг: RepeatAbility/AbilityChain карты, напр. Чертяга-король, просто стояли).
                    // Дохлый кастер (DeadTag) — не играем, чтобы не гнаться с его собственной анимацией смерти
                    // (тот же кейс гонки Cast/Death, что и у одиночного резолва).
                    if (targets.Length > 0 && spec != null && spec.PlayCasterAnimation && !world.GetPool<DeadTag>().Has(owner.CardEntity))
                    {
                        var casterView = GetCasterView(world, owner.CardEntity);
                        if (casterView != null && casterView.HasCastAnimation)
                        {
                            StartChainCasterAnim(world, entity, casterView);
                            continue;
                        }
                    }

                    if (targets.Length > 0 && spec != null && spec.Kind == VfxKind.Projectile && spec.Prefab != null && _boardView.Value != null)
                    {
                        UnityEngine.Debug.Log($"[ChainVfx] entity={entity} → LaunchStageProjectile");
                        LaunchStageProjectile(world, entity, owner.CardEntity, targets, spec);
                        continue;
                    }

                    FinishStage(world, entity, ref state, stages, ownerPool);
                }
                else
                {
                    // FixedInterval (см. ChainStage.AdvanceMode) — НЕ ждём осевшего мира между активациями:
                    // WorldSettled смотрит на ВЕСЬ мир (DeathAnimPendingTag/AttackAnimPendingTag и т.п.), не
                    // только на цели ЭТОЙ способности — если «Расстрелять» кого-то добивает по пути, ждать
                    // пришлось бы, пока полностью доиграет анимация смерти, прежде чем полетит следующий
                    // снаряд (баг 2026-08-24: залп fire-and-forget всё равно выглядел рваным — гейт запуска
                    // снаряда убрали, а этот, между активациями, забыли). Only таймер держит темп очереди.
                    bool fixedInterval = stages[state.Current] != null
                                          && stages[state.Current].Advance == ChainStage.AdvanceMode.FixedInterval;
                    if (!fixedInterval && !WorldSettled(world)) continue;   // ждём, пока урон/смерти прошлой стадии осядут
                    if (Time.time < state.NextAdvanceAt) continue;          // пауза читаемости (ActionPacing.GapSeconds)

                    state.Killed += CountDead(world, state.LastTargets);
                    state.Current++;
                    state.Applied = false;
                    state.LastTargets = null;

                    if (state.Current >= stages.Length)
                        statePool.Del(entity);
                }
            }
        }

        // Применяет эффекты стадии (мгновенно ИЛИ на прилёте снаряда/по таймауту) + косметику + снапшот,
        // помечает Applied. Цели читает из state.LastTargets (выставлены ДО ветвления снаряд/мгновенно —
        // одни и те же и для запуска снаряда, и для применения на прилёте).
        void FinishStage(EcsWorld world, int entity, ref ChainStateComponent state, ChainStage[] stages, EcsPool<AbilityOwnerComponent> ownerPool)
        {
            ref var owner = ref ownerPool.Get(entity);
            int card = owner.CardEntity;
            int abilityIndex = owner.AbilityIndex;
            var stage = stages[state.Current];
            var targets = state.LastTargets;

            ChainContext.CurrentKilled = state.Killed;   // контекст для эффектов стадии
            GeneratedCardChannel.ClearSent();            // соберём случайные генерации стадии для синка
            ApplyEffects(world, card, stage?.Effects, targets);
            EmitStageVfx(world, entity, card, owner.PlayerEntity, targets, stage);  // Hit/Beam/Area на цели ЭТОЙ стадии (см. докстринг метода)

            // СИНК: снапшот стадии (StepIndex + цели + KilledCount + выбранные случайные карты).
            PublishStageResolved(card, abilityIndex, state.Current, targets, state.Killed);

            state.Applied = true;
            // Пауза читаемости перед следующей стадией: ChainStage.GapSecondsOverride (>=0) переопределяет
            // общий ActionPacing.GapSeconds — нужно карточкам-очередям снарядов (Расстрелять), где реальное
            // время полёта VfxStep УЖЕ разносит выстрелы по времени, и полновесная пауза поверх этого мешает.
            float gap = stage != null && stage.GapSecondsOverride >= 0f ? stage.GapSecondsOverride : ActionPacing.GapSeconds;
            state.NextAdvanceAt = Time.time + gap;
        }

        // ── анимация кастера стадии ──────────────────────────────────────────

        // Живая CreatureView кастера — как в RunResolveAbilityQueueSystem.GetCasterView (см. её докстринг:
        // null, если карта не существо/визуал не заспавнен/уже скрыт).
        CreatureView GetCasterView(EcsWorld world, int caster)
        {
            if (!_viewPool.Value.Has(caster)) return null;
            var go = _viewPool.Value.Get(caster).View;
            if (go == null || !go.activeInHierarchy) return null;
            return go.GetComponent<CreatureView>();
        }

        // Запускает анимацию "Cast" на кастере СТАДИИ: вешает гейт (блокирует продвижение цепочки — см.
        // WorldSettled), резолв стадии откладывается до Animation Event'ов клипа (CastEvent/FinishEvent).
        void StartChainCasterAnim(EcsWorld world, int abilityEntity, CreatureView casterView)
        {
            ref var pending = ref world.GetPool<ChainCastAnimPendingComponent>().Add(abilityEntity);
            pending.Deadline = Time.time + AbilityAnimTimeout;
            pending.CastApplied = false;
            GameEventBus.Publish(new InputBlockedEvent());

            int token = abilityEntity;
            casterView.PlayAbilityCast(
                onCastPoint: () => _castPointReached.Enqueue(token),
                onFinished:  () => _animFinished.Enqueue(token));
        }

        // CastEvent пришёл (или форсирован таймаутом) → применить эффекты/запустить снаряд стадии РОВНО
        // один раз (CastApplied-гард) — та же точка, что дошла бы сюда и без анимации.
        void CompleteChainCastPoint(EcsWorld world, int abilityEntity)
        {
            var pool = world.GetPool<ChainCastAnimPendingComponent>();
            if (!pool.Has(abilityEntity)) return;
            ref var p = ref pool.Get(abilityEntity);
            if (p.CastApplied) return;
            p.CastApplied = true;
            ResolveOrLaunchStage(world, abilityEntity);
        }

        // FinishEvent пришёл (или форсирован таймаутом) → снять гейт. Страховка: если клип прислал ТОЛЬКО
        // FinishEvent (без CastEvent) — сначала всё равно применяем эффекты стадии.
        void CompleteChainAnimGate(EcsWorld world, int abilityEntity)
        {
            var pool = world.GetPool<ChainCastAnimPendingComponent>();
            if (!pool.Has(abilityEntity)) return;
            CompleteChainCastPoint(world, abilityEntity);
            pool.Del(abilityEntity);
            GameEventBus.Publish(new InputRestoredEvent());
        }

        // Общая точка «применить эффекты стадии ИЛИ запустить снаряд» — используется и мгновенным резолвом
        // (нет анимации кастера), и CastEvent-веткой (анимация кастера сыграла до момента применения).
        // Пересобирает state/stage заново по entity — сюда приходят из очереди колбэков, не из основного цикла.
        void ResolveOrLaunchStage(EcsWorld world, int abilityEntity)
        {
            var statePool = world.GetPool<ChainStateComponent>();
            var chainPool = world.GetPool<AbilityChainComponent>();
            var ownerPool = world.GetPool<AbilityOwnerComponent>();
            if (!statePool.Has(abilityEntity) || !chainPool.Has(abilityEntity) || !ownerPool.Has(abilityEntity)) return;

            ref var state = ref statePool.Get(abilityEntity);
            var stages = chainPool.Get(abilityEntity).Stages;
            if (stages == null || state.Current >= stages.Length) return;

            var targets = state.LastTargets ?? System.Array.Empty<int>();

            var vfxStepsPool = world.GetPool<AbilityVfxStepsComponent>();
            if (targets.Length > 0 && vfxStepsPool.Has(abilityEntity) && _boardView.Value != null)
            {
                var stepsList = vfxStepsPool.Get(abilityEntity).Steps;
                if (stepsList != null && stepsList.Count > 0)
                {
                    LaunchStageVfxSteps(world, abilityEntity, ownerPool.Get(abilityEntity).CardEntity, targets, stepsList);
                    return;
                }
            }

            var vfxPool = world.GetPool<AbilityVfxComponent>();
            var spec = vfxPool.Has(abilityEntity) ? vfxPool.Get(abilityEntity).Spec : null;

            if (targets.Length > 0 && spec != null && spec.Kind == VfxKind.Projectile && spec.Prefab != null && _boardView.Value != null)
            {
                LaunchStageProjectile(world, abilityEntity, ownerPool.Get(abilityEntity).CardEntity, targets, spec);
                return;
            }

            FinishStage(world, abilityEntity, ref state, stages, ownerPool);
        }

        // Запуск снаряда стадии: ОДНО ProjectileVfxEvent на ВСЕ цели стадии, вешает ChainProjectilePendingComponent.
        void LaunchStageProjectile(EcsWorld world, int abilityEntity, int caster, int[] targets, VfxSpec spec)
        {
            VfxEmitUtil.LaunchProjectile(world, _boardView.Value, spec, caster, targets, abilityEntity);

            ref var pending = ref world.GetPool<ChainProjectilePendingComponent>().Add(abilityEntity);
            pending.Deadline = Time.time + ProjectileTimeout;
        }

        // Старт VFX-таймлайна стадии (VfxSteps) — тот же VfxStepsPendingComponent/VfxEmitUtil.TryLaunchDueSteps,
        // что у RunResolveAbilityQueueSystem.LaunchVfxSteps; ждём его в ветке vfxStepsPendingPool.Has(entity)
        // основного цикла (Run()) — она сама зовёт FinishStage, когда всё запущено и долетело.
        void LaunchStageVfxSteps(EcsWorld world, int abilityEntity, int caster, int[] targets, List<VfxStep> steps)
        {
            ref var pending = ref world.GetPool<VfxStepsPendingComponent>().Add(abilityEntity);
            pending.ResolveStartTime = Time.time;
            pending.Launched = new bool[steps.Count];
            pending.PendingArrivals = 0;

            var ownerPool = world.GetPool<AbilityOwnerComponent>();
            int ownerPlayer = ownerPool.Has(abilityEntity) ? ownerPool.Get(abilityEntity).PlayerEntity : -1;
            VfxEmitUtil.TryLaunchDueSteps(world, _boardView.Value, abilityEntity, caster, ownerPlayer, targets, steps, ref pending);
        }

        // FixedInterval (ChainStage.AdvanceMode) — косметика VfxSteps БЕЗ гейта ожидания: ResolveStartTime
        // искусственно в прошлом, поэтому TryLaunchDueSteps считает все StartDelay уже прошедшими и
        // запускает шаги немедленно. throwaway.PendingArrivals намеренно не сохраняем никуда — вызывающий
        // не ждёт, а когда VfxPresenter всё же опубликует VfxArrivedEvent{Token=abilityEntity}, верхний
        // цикл Run() не найдёт для этой сущности РЕАЛЬНОГО VfxStepsPendingComponent (мы его не завели) и
        // молча пропустит декремент — снаряд просто долетает сам по себе, никого не блокируя.
        void FireAndForgetVfxSteps(EcsWorld world, int abilityEntity, int caster, int ownerPlayer, int[] targets, List<VfxStep> steps)
        {
            var throwaway = new VfxStepsPendingComponent
            {
                ResolveStartTime = Time.time - 999f,
                Launched = new bool[steps.Count],
                PendingArrivals = 0,
            };
            VfxEmitUtil.TryLaunchDueSteps(world, _boardView.Value, abilityEntity, caster, ownerPlayer, targets, steps, ref throwaway);
        }

        // То же самое для легаси Vfx (не VfxSteps) — token<0 у LaunchProjectile означает «чистая косметика
        // без арривал-сигнала» (см. докстринг VfxEmitUtil.LaunchProjectile, тот же режим уже используется
        // пассивным реплеем).
        void FireAndForgetLegacyVfx(EcsWorld world, int caster, int[] targets, VfxSpec spec)
        {
            if (spec == null) return;
            if (spec.Kind == VfxKind.Projectile && spec.Prefab != null)
                VfxEmitUtil.LaunchProjectile(world, _boardView.Value, spec, caster, targets, token: -1);
            else
                VfxEmitUtil.EmitInstantVfx(world, _boardView.Value, spec, caster, targets);
        }

        static void PublishStageResolved(int card, int abilityIndex, int step, int[] targets, int killed)
        {
            var sent = GeneratedCardChannel.Sent;
            string[] exps = null;
            int[] ids = null;
            if (sent.Count > 0)
            {
                exps = new string[sent.Count];
                ids = new int[sent.Count];
                for (int i = 0; i < sent.Count; i++) { exps[i] = sent[i].exp; ids[i] = sent[i].cardId; }
            }

            GameEventBus.Publish(new AbilityResolvedNetEvent
            {
                SourceCardEntity      = card,
                AbilityIndex          = abilityIndex,
                TargetEntities        = targets,
                StepIndex             = step,
                KilledCount           = killed,
                GeneratedExpansionIds = exps,
                GeneratedCardIds      = ids,
            });
        }

        // ── targeting ─────────────────────────────────────────────────────────

        static int[] ResolveTargets(EcsWorld world, ChainStage stage, int casterCard, int casterPlayer)
        {
            if (stage == null) return new[] { casterPlayer };

            var filters = stage.Filters != null ? stage.Filters.ToArray() : null;

            switch (stage.Mode)
            {
                case ChainStage.TargetingMode.Field:
                {
                    var list = TargetGather.Gather(world, filters, casterCard, casterPlayer, stage.Area);
                    return list.ToArray();
                }
                case ChainStage.TargetingMode.Target:
                {
                    // Zone (Hand/Deck/Grave — «сбросить дешёвую из руки») + честь Selection (LeastExpensive и т.п.),
                    // раньше цепочка всегда брала Board+Random. Selected/TriggerSubject в цепочке не поддержаны → Random.
                    var list = TargetGather.Gather(world, filters, casterCard, casterPlayer, null, stage.Zone);
                    switch (stage.Selection)
                    {
                        case TargetSelection.LeastExpensive: return RunAbilityTargetingSystem.PickByCost(world, list, stage.Count, mostExpensive: false);
                        case TargetSelection.MostExpensive:  return RunAbilityTargetingSystem.PickByCost(world, list, stage.Count, mostExpensive: true);
                        case TargetSelection.Strongest:      return RunAbilityTargetingSystem.PickStrongest(world, list, stage.Count);
                        case TargetSelection.MostWounded:    return RunAbilityTargetingSystem.PickMostWounded(world, list, stage.Count);
                        default:                             return PickRandom(world, list, stage.Count);
                    }
                }
                default:
                    return new[] { casterPlayer };   // NonTarget
            }
        }

        static void ApplyEffects(EcsWorld world, int card, List<IEffect> effects, int[] targets)
        {
            if (effects == null || targets == null) return;
            foreach (var target in targets)
                foreach (var eff in effects)
                    if (eff != null && eff.IsReady)
                        eff.Apply(world, card, target);
        }

        // ── vfx ───────────────────────────────────────────────────────────────

        // Косметика стадии (Hit/Beam/Area) — RunChainSystem раньше ВООБЩЕ не читал AbilityVfxComponent
        // (баг 2026-08-11: у RepeatAbility/AbilityChain карт с Vfx.HitPrefab визуал молча не играл — только
        // обычный нецепочечный резолв в RunResolveAbilityQueueSystem.EmitVfx умел эту косметику). Один и
        // тот же AbilityVfxComponent (авторится ОДИН раз на способность) проигрывается на КАЖДОЙ стадии —
        // так N независимых активаций RepeatAbility каждая получает свою вспышку на СВОЕЙ цели.
        // ПРОЕКТИЛЬ — отдельной веткой (LaunchStageProjectile, до применения эффектов), сюда не попадает:
        // тому нужна асинхронная доставка + гейт ожидания прилёта, см. докстринг класса.
        void EmitStageVfx(EcsWorld world, int abilityEntity, int caster, int casterPlayer, int[] targets, ChainStage stage)
        {
            var vfxPool = world.GetPool<AbilityVfxComponent>();
            if (!vfxPool.Has(abilityEntity)) return;

            // NonTarget-стадия технически целит в САМОГО КАСТЕРА (ResolveTargets: нет цели → [casterPlayer]) —
            // бить визуально почти всегда нечего. Общий Vfx способности (один на ВСЮ цепочку, см. докстринг
            // класса) иначе играет на аватаре кастера и для чисто бухгалтерских стадий («Дать газу!»: 2-я
            // стадия просто замешивает карты в колоду оппонента, ничего не «бьёт») — см. ForceVfxOnNonTarget,
            // если конкретной стадии VFX на кастере всё же нужен.
            if (stage == null || (stage.Mode == ChainStage.TargetingMode.NonTarget && !stage.ForceVfxOnNonTarget)) return;

            // Field-стадия (RepeatAbility «по всем врагам/своим» и т.п.) — Area-VFX красит ЗОНУ (половину/
            // всё поле), а не баунды фактических целей (см. VfxEmitUtil.ZoneBounds).
            Bounds? zone = stage != null && stage.Mode == ChainStage.TargetingMode.Field
                ? VfxEmitUtil.ZoneBounds(world, _boardView.Value, stage.Area, caster, casterPlayer)
                : null;

            VfxEmitUtil.EmitInstantVfx(world, _boardView.Value, vfxPool.Get(abilityEntity).Spec, caster, targets, zone);
        }

        // ── settle / death count ─────────────────────────────────────────────

        static bool WorldSettled(EcsWorld world)
        {
            if (world.Filter<TakeDamageEvent>().End().GetEntitiesCount() > 0) return false;        // урон не применён
            if (world.Filter<DeadTag>().Inc<BoardTag>().End().GetEntitiesCount() > 0) return false; // смерти не обработаны
            if (world.Filter<MovingTag>().End().GetEntitiesCount() > 0) return false;
            if (world.Filter<AttackAnimPendingTag>().End().GetEntitiesCount() > 0) return false;
            if (world.Filter<DeathAnimPendingTag>().End().GetEntitiesCount() > 0) return false;     // анимация смерти ещё доигрывает
            if (world.Filter<ChainCastAnimPendingComponent>().End().GetEntitiesCount() > 0) return false; // анимация каста стадии ещё доигрывает
            return true;
        }

        static int CountDead(EcsWorld world, int[] targets)
        {
            if (targets == null) return 0;
            var dead = world.GetPool<DeadTag>();
            var hp = world.GetPool<HealthComponent>();
            var board = world.GetPool<BoardTag>();
            var creature = world.GetPool<CreatureTag>();
            int n = 0;
            foreach (var t in targets)
            {
                if (dead.Has(t)) { n++; continue; }
                if (hp.Has(t) && hp.Get(t).Current <= 0) { n++; continue; }
                // Командир: DieSystem лечит его и снимает DeadTag В ТОМ ЖЕ кадре (ReturnCommanderToHand) —
                // к моменту WorldSettled он снова «жив» по DeadTag/HP, обычные проверки выше его не видят
                // (баг: Дать газу не замешивало Вонючее облако, добив командира — RepeatEffect{Killed}
                // насчитывал 0). Но BoardTag снимается ПРИ ЛЮБОЙ смерти безусловно (и обычной, и
                // командирской), а вернуться на стол в рамках ЭТОГО ЖЕ резолва стадии командир не может
                // (возврат — только в руку, повторный розыгрыш отдельным действием игрока). «Цель стадии
                // была существом, а сейчас без BoardTag» — надёжный признак смерти для обоих случаев.
                // Гейт CreatureTag — не считать так игроков-аватаров (у тех BoardTag нет вообще, не из-за смерти).
                if (creature.Has(t) && !board.Has(t)) n++;
            }
            return n;
        }

        static int[] PickRandom(EcsWorld world, List<int> candidates, int count)
        {
            if (count <= 0 || candidates.Count == 0) return System.Array.Empty<int>();
            if (candidates.Count <= count) return candidates.ToArray();

            // [SyncWatch] см. тот же лог в RunAbilityTargetingSystem.PickRandom — цепочка должна крутиться
            // только на активе; если этот путь дошёл до пассива, стадии цепочки разойдутся молча.
            if (!TurnGate.IsLocalActive(world))
                UnityEngine.Debug.LogError("[SyncWatch] RunChainSystem.PickRandom вызван НЕ на активном клиенте — цели стадии разойдутся (десинк).");

            for (int i = 0; i < count; i++)   // частичный Фишер-Йейтс; детерминизм для синка — TODO
            {
                int j = UnityEngine.Random.Range(i, candidates.Count);
                (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
            }
            var res = new int[count];
            candidates.CopyTo(0, res, 0, count);
            return res;
        }
    }
}
