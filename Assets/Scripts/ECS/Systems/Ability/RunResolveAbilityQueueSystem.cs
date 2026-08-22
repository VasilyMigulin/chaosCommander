using System;
using System.Collections.Generic;
using Game.Core.Configs;
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
    /// Разрешает очередь способностей ПО ОДНОЙ за тик: берёт первую AbilityQueuedState-сущность,
    /// применяет её эффекты (AbilityEffectContainerComponent) к каждой цели (если IsReady) и снимает state.
    /// Кастер берётся из AbilityOwnerComponent.
    ///
    /// АНИМАЦИЯ КАСТЕРА (opt-in, VfxSpec.PlayCasterAnimation=true + кастер — существо с параметром "Cast" в
    /// аниматоре): резолв НЕ происходит мгновенно на выборке из очереди — кастер играет анимацию "Cast"
    /// (CreatureView.PlayAbilityCast), эффекты применяются на Animation Event "CastEvent" (авторит
    /// гейм-дизайнер в клипе), а АЛИБИЛИТИ-ГЕЙТ (AbilityAnimPendingComponent, блокирует очередь/таймер, как
    /// AttackAnimPendingTag) снимается на "FinishEvent". Анти-софтлок: Deadline форсит оба, если клип их не
    /// прислал. Без флага/аниматора — резолв мгновенный, КАК РАНЬШЕ (нулевой риск для уже собранных карт).
    ///
    /// ДОСТАВКА СНАРЯДА: если у способности VfxKind.Projectile — эффекты НЕ применяются сразу: запускаем
    /// снаряд (на CastEvent анимации кастера, если она играется, иначе сразу на выборке из очереди), вешаем
    /// AbilityCastPendingComponent (гейт очереди + реплея, как AttackAnimPendingTag) и ждём VfxArrivedEvent
    /// от VfxPresenter (на прилёте). Снапшот (AbilityResolvedNetEvent) шлётся ТОЖЕ на прилёте — иначе
    /// случайные роллы (GeneratedCardChannel) уйдут раньше применения → рассинхрон. Beam/Area/без-VFX —
    /// мгновенно (на CastEvent, если анимация кастера играется). Анти-софтлок: Deadline форсит резолв, если
    /// прилёт не пришёл. Синк: каждый клиент гейтит свой снаряд/анимацию и применяет на своём прилёте/
    /// CastEvent; порядок действий фиксирован снапшот-очередью → детерминизм сохраняется (анимация кастера
    /// косметическая — на game-state не влияет, только на МОМЕНТ применения на КАЖДОМ клиенте локально).
    /// </summary>
    public sealed class RunResolveAbilityQueueSystem : IEcsInitSystem, IEcsRunSystem, IEcsDestroySystem
    {
        readonly EcsCustomInject<BoardView> _boardView = default;
        readonly EcsCustomInject<DefaultAbilityVfxConfig> _defaultVfx = default;
        readonly EcsPoolInject<ViewRefComponent> _viewPool = default;

        const float ProjectileTimeout = 4f;   // сек до форс-резолва, если прилёт не пришёл

        readonly Queue<int> _arrived = new Queue<int>();          // токены приземлившихся снарядов (ability-сущности)
        readonly Queue<int> _castPointReached = new Queue<int>(); // CastEvent анимации кастера (ability-сущности)
        readonly Queue<int> _animFinished = new Queue<int>();     // FinishEvent анимации кастера (ability-сущности)
        bool _subscribed;
        float _nextResolveAt;   // не берём следующую способность из очереди раньше этого времени (ActionPacing)

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
            var pendingPool = world.GetPool<AbilityCastPendingComponent>();
            var animPendingPool = world.GetPool<AbilityAnimPendingComponent>();

            // 0) ГРАНИЦА ОКНА ПРИЧИННОСТИ. Реакция наследует ключ последней отрезолвленной способности —
            // но только пока каскад ИДЁТ. Очередь опустела → каскад закончился, и следующая реакция
            // (напр. хрип существа, убитого обычной атакой) не должна цепляться к способности из прошлого
            // каскада: её ключ со старой волной вклинил бы реакцию ПЕРЕД свежими активациями.
            if (world.Filter<AbilityQueuedState>().End().GetEntitiesCount() == 0
                && world.Filter<AbilityCastPendingComponent>().End().GetEntitiesCount() == 0
                && world.Filter<AbilityAnimPendingComponent>().End().GetEntitiesCount() == 0)
            {
                // Записки о причине НА КАРТАХ тут НЕ трогаем: пиньята создаёт 10 карт разом, очередь
                // между их розыгрышами опустевает, и зачистка здесь стёрла бы им родителя. Их снимает
                // граница хода (CauseStamp.ClearAll в RunTurnStartSystem).
                AbilityResolveContext.ClearCause();
            }

            // 1) Приземлившиеся снаряды → применить отложенные эффекты.
            while (_arrived.Count > 0)
            {
                int token = _arrived.Dequeue();
                if (token >= 0 && pendingPool.Has(token)) LandAndResolve(world, token);
            }

            // 2) Анти-софтлок снарядов: форсим резолв просроченных «в полёте» (нет VfxPresenter/префаба).
            foreach (var e in world.Filter<AbilityCastPendingComponent>().End())
            {
                if (pendingPool.Get(e).Deadline <= Time.time) { LandAndResolve(world, e); break; }
            }

            // 3) CastEvent анимации кастера → применить эффекты (или запустить снаряд — та же ветка, что и
            //    без анимации). FinishEvent → снять гейт анимации (страхуем CastEvent, если клип его не прислал).
            while (_castPointReached.Count > 0) CompleteCastPoint(world, animPendingPool, _castPointReached.Dequeue());
            while (_animFinished.Count > 0)     CompleteAnimGate(world, animPendingPool, _animFinished.Dequeue());

            // Анти-софтлок анимации кастера: клип не прислал CastEvent/FinishEvent — форсим оба.
            foreach (var e in world.Filter<AbilityAnimPendingComponent>().End())
            {
                if (animPendingPool.Get(e).Deadline <= Time.time) { CompleteAnimGate(world, animPendingPool, e); break; }
            }

            // 4) Пока что-то в полёте/анимируется — следующее действие не берём (гейт, как у атаки).
            if (world.Filter<AbilityCastPendingComponent>().End().GetEntitiesCount() > 0) return;
            if (world.Filter<AbilityAnimPendingComponent>().End().GetEntitiesCount()  > 0) return;

            // 4b) Базовая пауза между соседними резолвами очереди: читаемость каскада эффектов + снапшоты
            //     (AbilityResolvedNetEvent) не улетают пачкой в соседние кадры. Анимацию/снаряд не трогает
            //     (те выше) — гейтит только «взять СЛЕДУЮЩУЮ» способность.
            if (Time.time < _nextResolveAt) return;

            // 5) Обычный разбор очереди — одна способность за тик. ПОРЯДОК (классика ККИ, требование юзера
            //    2026-07-29): раньше брался «первый в фильтре» — после свап-удалений ecslite порядок
            //    перемешивался, и каскад начала/конца хода резолвился хаотично («последний вышедший бил
            //    первым»). Теперь выбираем МИНИМУМ по ключу (Wave — кадр постановки: волны FIFO;
            //    внутри волны — порядок ВЫХОДА карты на стол (BoardEntryOrder, «первым вышел — первым
            //    активировал»; карта не на столе/спелл = 0 → раньше реакций поля); затем AbilityIndex).
            //    Сортировка только у АКТИВА — пассив реплеит его порядок из снапшотов.
            var queuedPool = world.GetPool<AbilityQueuedState>();
            var ownerPool  = world.GetPool<AbilityOwnerComponent>();

            int first = -1;
            ActivationKey best = default;
            foreach (var entity in world.Filter<AbilityQueuedState>().Inc<AbilityOwnerComponent>().End())
            {
                ref var q = ref queuedPool.Get(entity);
                if (first < 0 || q.Key.CompareTo(best) < 0) { first = entity; best = q.Key; }
            }
            if (first < 0) return;

            // Эта активация становится ПРИЧИНОЙ для реакций, которые она породит (хрип убитого встанет
            // сразу за ней, а не в конец очереди). Ставим ДО применения эффектов и не чистим в finally:
            // смерть от урона обрабатывается только в следующем кадре — см. AbilityResolveContext.
            AbilityResolveContext.BeginResolve(best);

            // Анимация кастера (opt-in): если запрошена И у кастера реально есть параметр "Cast" —
            // откладываем резолв до CastEvent/FinishEvent. Иначе — мгновенно, как раньше.
            // ВАЖНО: только если у способности реально ЕСТЬ цели — Field/Target-таргетинг кладёт в очередь
            // ПУСТОЙ массив, когда подходящих кандидатов нет (напр. Селекционер: все союзники уже забаффаны),
            // и без этой проверки кастер всё равно проигрывал бы анимацию/VFX «в никуда» (эффект и так не
            // применится, но выглядело бы как сработавшая способность). NonTarget/Self всегда дают ≥1 цель
            // (игрок-владелец/сам кастер), так их анимация не задевается.
            // КАСТЕР УЖЕ МЁРТВ (DeadTag): GetCasterView проверяет только activeInHierarchy — если кастер
            // погиб МГНОВЕНИЕ назад (напр. добит чужой способностью того же конца хода) и его вьюха ещё
            // физически активна (Death-анимация доигрывает), она пройдёт эту проверку. PlayAbilityCast тогда
            // перезапишет _currentFinish (ОБЩЕЕ поле конца анимации — см. CreatureView) поверх Death'ового
            // Finish-колбэка → HideAfterDeath/DeathAnimPendingTag-снятие никогда не вызовутся, а вместо
            // анимации смерти кастер молча триггерит "Cast" (баг: Мини-черт исчезает без анимации смерти,
            // если чужой рандом-урон конца хода добивает его, пока его СОБСТВЕННАЯ способность ещё в очереди).
            var deadPoolCheck = world.GetPool<DeadTag>();
            bool casterAlive = !deadPoolCheck.Has(ownerPool.Get(first).CardEntity);
            var vfxPoolCheck = world.GetPool<AbilityVfxComponent>();
            bool hasTargets = (queuedPool.Get(first).Targets?.Length ?? 0) > 0;
            if (casterAlive && hasTargets && vfxPoolCheck.Has(first) && vfxPoolCheck.Get(first).Spec?.PlayCasterAnimation == true)
            {
                var casterView = GetCasterView(world, ownerPool.Get(first).CardEntity);
                if (casterView != null && casterView.HasCastAnimation)
                {
                    StartCasterAnim(world, first, casterView);
                    return;
                }
            }

            ResolveOrLaunch(world, first);
        }

        // Живая CreatureView кастера (существо на поле, объект активен) — null, если карта не существо/
        // визуал не заспавнен/уже скрыт (напр. умерло раньше резолва).
        CreatureView GetCasterView(EcsWorld world, int caster)
        {
            if (!_viewPool.Value.Has(caster)) return null;
            var go = _viewPool.Value.Get(caster).View;
            if (go == null || !go.activeInHierarchy) return null;
            return go.GetComponent<CreatureView>();
        }

        const float AbilityAnimTimeout = 4f;   // анти-софтлок (как ProjectileTimeout) на случай кривой разметки клипа

        // Запускает анимацию "Cast" на кастере: вешает гейт (блокирует очередь/таймер), резолв откладывается
        // до Animation Event'ов клипа (CastEvent/FinishEvent через CreatureAnimationRelay).
        void StartCasterAnim(EcsWorld world, int ability, CreatureView casterView)
        {
            ref var pending = ref world.GetPool<AbilityAnimPendingComponent>().Add(ability);
            pending.Deadline = Time.time + AbilityAnimTimeout;
            pending.CastApplied = false;
            GameEventBus.Publish(new InputBlockedEvent());

            int token = ability;   // замыкание — БЕЗ мутации ECS-состояния из колбэка (см. паттерн _arrived)
            casterView.PlayAbilityCast(
                onCastPoint: () => _castPointReached.Enqueue(token),
                onFinished:  () => _animFinished.Enqueue(token));

            Debug.Log($"[Resolve] ability={ability} играет анимацию каста на кастере (ждём CastEvent/FinishEvent)");
        }

        // CastEvent пришёл (или форсирован таймаутом) → применить эффекты РОВНО ОДИН раз (CastApplied-гард).
        void CompleteCastPoint(EcsWorld world, EcsPool<AbilityAnimPendingComponent> pool, int ability)
        {
            if (!pool.Has(ability)) return;
            ref var p = ref pool.Get(ability);
            if (p.CastApplied) return;
            p.CastApplied = true;
            ResolveOrLaunch(world, ability);
        }

        // FinishEvent пришёл (или форсирован таймаутом) → снять гейт анимации. Страховка: если клип прислал
        // ТОЛЬКО FinishEvent (без CastEvent) — сначала всё равно применяем эффекты (иначе способность молча
        // пропадёт без резолва).
        void CompleteAnimGate(EcsWorld world, EcsPool<AbilityAnimPendingComponent> pool, int ability)
        {
            if (!pool.Has(ability)) return;
            CompleteCastPoint(world, pool, ability);
            pool.Del(ability);
            GameEventBus.Publish(new InputRestoredEvent());
        }

        // Общая точка «применить эффекты ИЛИ запустить снаряд» — используется и мгновенным резолвом (нет
        // анимации кастера), и CastEvent-веткой (анимация кастера сыграла до момента применения).
        void ResolveOrLaunch(EcsWorld world, int first)
        {
            var queuedPool = world.GetPool<AbilityQueuedState>();
            var ownerPool  = world.GetPool<AbilityOwnerComponent>();
            if (!ownerPool.Has(first) || !queuedPool.Has(first)) return;

            var vfxPool = world.GetPool<AbilityVfxComponent>();
            int[] targets = queuedPool.Get(first).Targets ?? Array.Empty<int>();

            if (_boardView.Value != null && vfxPool.Has(first) && targets.Length > 0)
            {
                var spec = vfxPool.Get(first).Spec;
                if (spec != null && spec.Kind == VfxKind.Projectile && spec.Prefab != null)
                {
                    LaunchProjectile(world, first, ownerPool.Get(first).CardEntity, targets, spec);
                    return;
                }
            }

            ResolveAbility(world, first);
        }

        // Запуск снаряда: ОДНО ProjectileVfxEvent на ВСЕ цели разом (Delivery решает, лететь параллельно
        // или эстафетой — презентер), вешаем pending (гейт) + блок инпута. Эффекты НЕ применяем и
        // AbilityQueuedState НЕ снимаем — ждём ОДНОГО VfxArrivedEvent по завершении всей доставки.
        void LaunchProjectile(EcsWorld world, int ability, int caster, int[] targets, VfxSpec spec)
        {
            VfxEmitUtil.LaunchProjectile(world, _boardView.Value, spec, caster, targets, ability);

            ref var pending = ref world.GetPool<AbilityCastPendingComponent>().Add(ability);
            pending.Deadline = Time.time + ProjectileTimeout;
            GameEventBus.Publish(new InputBlockedEvent());
            Debug.Log($"[Resolve] projectile launched ability={ability} targets={targets.Length} (ждём хит)");
        }

        /// <summary>
        /// Списать заряд лимита активаций (Ability.MaxActivationsPerTurn). Заряд ОБЩИЙ для всех способностей
        /// карты с тем же LimitGroup: под капотом «если жёлтая — копия, иначе — добор» это две способности,
        /// а для игрока — ОДНА, и «раз в ход» должно считаться суммарно (решение юзера 2026-07-30).
        /// Group=0 (умолчание) = вся карта; разные номера — независимые лимиты внутри карты.
        /// </summary>
        static void SpendUseCharge(EcsWorld world, int abilityEntity, int cardEntity)
        {
            var limitPool = world.GetPool<AbilityUseLimitComponent>();
            if (!limitPool.Has(abilityEntity)) return;
            int group = limitPool.Get(abilityEntity).Group;

            var containerPool = world.GetPool<AbilityContainerComponent>();
            if (!containerPool.Has(cardEntity))
            {
                limitPool.Get(abilityEntity).UsedThisTurn++;
                return;
            }

            var siblings = containerPool.Get(cardEntity).AbilityEntities;
            if (siblings == null) { limitPool.Get(abilityEntity).UsedThisTurn++; return; }

            foreach (var ae in siblings)
            {
                if (!limitPool.Has(ae)) continue;
                ref var l = ref limitPool.Get(ae);
                if (l.Group == group) l.UsedThisTurn++;   // общий счёт группы (одна «игровая» способность)
            }
        }

        // Прилёт снаряда (или форс по таймауту): снимаем pending, разблокируем инпут, применяем эффекты.
        // skipRecompute=true — снаряд уже ФИЗИЧЕСКИ долетел до конкретной, заранее выбранной цели; нельзя
        // пере-роллить её здесь (см. ResolveAbility).
        void LandAndResolve(EcsWorld world, int ability)
        {
            world.GetPool<AbilityCastPendingComponent>().Del(ability);
            GameEventBus.Publish(new InputRestoredEvent());
            ResolveAbility(world, ability, skipRecompute: true);
        }

        // Применение эффектов + сбор синка + снапшот + косметика (Beam/Area). Вызывается мгновенно
        // (Beam/Area/без-VFX) или на прилёте снаряда (Projectile).
        void ResolveAbility(EcsWorld world, int first, bool skipRecompute = false)
        {
            var queuedPool = world.GetPool<AbilityQueuedState>();
            var ownerPool  = world.GetPool<AbilityOwnerComponent>();
            var effectPool = world.GetPool<AbilityEffectContainerComponent>();

            if (!ownerPool.Has(first) || !queuedPool.Has(first)) return;

            ref var owner = ref ownerPool.Get(first);
            int caster = owner.CardEntity;
            int abilityIndex = owner.AbilityIndex;
            int playerEntity = owner.PlayerEntity;   // владелец способности — для caster-scoped эффектов
            ref var queued = ref queuedPool.Get(first);
            int[] targets = queued.Targets ?? Array.Empty<int>();

            // Цели пересобираем В МОМЕНТ РЕЗОЛВА (не замороженный снэпшот таргетинга) для ВСЕХ неинтерактивных
            // выборов — Field / Random / Strongest / дешевейший. Иначе существо, ПРИЗВАННОЕ другой способностью
            // РАНЬШЕ в этой же пачке (OnTurnStart: чара-призыв резолвится кадром раньше → CreateCardSystem уже
            // поставил его на доску), не попало бы ни под бафф-по-области, ни в рулетку «случайному существу».
            // Так соблюдается порядок: призыв раньше → существо учитывается; бафф/урон раньше → нет (его ещё нет).
            // ТОЛЬКО на АКТИВЕ: пассив реплеит финальный набор целей по ключам (AbilityResolvedNetEvent) — пересбор
            // у него дал бы рассинхрон. Selected/TriggerSubject/NonTarget/Self пересбор НЕ трогает (вернёт null).
            //
            // skipRecompute (баг 2026-08-11): снаряд (LaunchProjectile) уже отправлен ЛЕТЕТЬ в targets ДО
            // этого вызова — для Random-выбора пересбор здесь означает ВТОРОЙ независимый бросок кубика
            // (не «подтверждение» той же цели, а новый ролл), из-за чего эффект резолвился в ДРУГУЮ цель,
            // чем та, куда визуально прилетел снаряд. LandAndResolve (вызывается ТОЛЬКО на прилёте снаряда/
            // по таймауту) просит пропустить пересбор — targets остаются ТЕМИ ЖЕ, что летели.
            if (!skipRecompute && TurnGate.IsLocalActive(world))
            {
                var recomputed = RunAbilityTargetingSystem.RecomputeNonInteractive(world, first);
                if (recomputed != null) targets = recomputed;
            }

            // Ключ в логе — по нему порядок каскада читается прямо из консоли, без спец-сценария:
            // корень выглядит как (волна,выход,индекс), следствие дописывает уровни через «·».
            // Вложенность = отступ, поэтому дерево видно глазами (см. ActivationKey).
            string indent = new string(' ', queuedPool.Get(first).Key.Depth * 2);
            Debug.Log($"[Resolve] {indent}key={queuedPool.Get(first).Key} card={caster} "
                    + $"abilityIdx={abilityIndex} targets={targets.Length}");

            // Скрэтч призванных за этот резолв (для синка модификаторов призыва).
            SummonScratch.Clear();

            // Инициатор резолва (атрибуция генерации «кем замешано»).
            var originPool = world.GetPool<AbilityOriginComponent>();
            AbilityResolveContext.OriginOwnerId = originPool.Has(first) ? originPool.Get(first).OriginOwnerId : -1;

            // Ключ триггера ЭТОЙ активации (PlayTargetCardEffect/PlaySameNameFromHandEffect: интерактивный
            // выбор цели только от OnCast — см. AbilityResolveContext.TriggerKey). Копия в локальную переменную
            // — ниже она нужна ПОСЛЕ finally, который зовёт AbilityResolveContext.Clear() (обнуляет TriggerKey);
            // без локальной копии EmitDefaultTriggerVfx всегда видел бы null (баг: дефолтные VFX для OnCast/
            // OnDie никогда не срабатывали, независимо от того, назначен ли DefaultAbilityVfxConfig).
            var triggerKeyPool = world.GetPool<AbilityTriggerKeyComponent>();
            string triggerKey = triggerKeyPool.Has(first) ? triggerKeyPool.Get(first).Key : null;
            bool isSelfTrigger = triggerKeyPool.Has(first) && triggerKeyPool.Get(first).IsSelfTrigger;
            AbilityResolveContext.TriggerKey = triggerKey;
            AbilityResolveContext.IsSelfTrigger = isSelfTrigger;

            // Счётчик применений ЭТОЙ способности (Нечищенный источник: 1,2,3… маны через
            // RepeatEffect{SelfResolves}). Инкремент ДО эффектов → текущее применение = 1 на первом
            // срабатывании. Пассив резолвит впрыснутую очередь здесь же → зеркально, синк не нужен.
            var resolveCountPool = world.GetPool<AbilityResolveCounterComponent>();
            if (!resolveCountPool.Has(first)) resolveCountPool.Add(first);
            AbilityResolveContext.ResolveCount = ++resolveCountPool.Get(first).Count;

            // Синк недетерм. генерации: пассив грузит идентичности активного, актив роллит и Record'ит.
            GeneratedCardChannel.ClearSent();
            GeneratedCardChannel.LoadReplay(queued.GeneratedExps, queued.GeneratedIds);

            // Очередь обязана очиститься, что бы ни случилось (иначе резолв спамит каждый кадр).
            try
            {
                if (effectPool.Has(first))
                {
                    var effects = effectPool.Get(first).Effects;
                    if (effects != null)
                    {
                        // Лимит активаций за ход: тратим заряд ТОЛЬКО на фактическом применении — холостой
                        // файр (фильтры не дали целей) лимит не съедает. NonTarget/Self всегда имеют цель.
                        if (targets.Length > 0) SpendUseCharge(world, first, caster);

                        // Caster-scoped эффекты (добор/золото/мана/кост владельцу) — РОВНО ОДИН раз за резолв,
                        // target = игрок-владелец. Иначе в мультицельной способности (Field/Random с N целями)
                        // они отработали бы N раз. Для NonTarget (target=[владелец]) результат тот же.
                        foreach (var effect in effects)
                            if (effect is ICasterScopedEffect && effect.IsReady)
                                effect.Apply(world, caster, playerEntity);

                        // Остальные эффекты — по каждой цели.
                        foreach (var target in targets)
                            foreach (var effect in effects)
                                if (effect != null && effect.IsReady && !(effect is ICasterScopedEffect))
                                    effect.Apply(world, caster, target);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[Resolve] effect apply failed (ability={first} card={caster}): {e}");
            }
            finally
            {
                AbilityResolveContext.Clear();
                // Множитель частоты (Временная петля): пока Remaining > 1 — НЕ снимаем queued (повторится).
                var pcPool = world.GetPool<PendingCastsComponent>();
                if (pcPool.Has(first) && pcPool.Get(first).Remaining > 1)
                {
                    pcPool.Get(first).Remaining--;
                }
                else
                {
                    if (pcPool.Has(first)) pcPool.Del(first);

                    // Довесок (баг 2026-08-21, «Посредственное руководство»): способность сработала повторно
                    // за НОВОГО виновника, пока ЭТОТ резолв ещё стоял в очереди (резолв — по одной способности
                    // за тик, первая легко залёживается) — RunAbilityTargetingSystem.Queue не даёт стереть
                    // текущие Targets, а копит следующие сюда (см. AbilityQueuedState.PendingTargets). Сняв
                    // текущий state, сразу ставим следующий — БЕЗ похода через AbilityCastEvent/повторный
                    // таргетинг (цели уже посчитаны на момент ТОГО срабатывания, как и у обычного Queue).
                    var pendingTargets = queuedPool.Has(first) ? queuedPool.Get(first).PendingTargets : null;
                    if (queuedPool.Has(first)) queuedPool.Del(first);

                    if (pendingTargets != null && pendingTargets.Count > 0)
                    {
                        int[] nextTargets = pendingTargets[0];
                        pendingTargets.RemoveAt(0);
                        ref var q2 = ref queuedPool.Add(first);
                        q2.Targets = nextTargets;
                        q2.Key = RunAbilityTargetingSystem.BuildKey(world, first);
                        q2.PendingTargets = pendingTargets.Count > 0 ? pendingTargets : null;
                    }
                    else
                    {
                        // Очередь виновников (TriggerSubject): RunAbilityTargetingSystem (AdvanceOrClearSubject)
                        // оставляет TriggerSubjectComponent живым, если для этой же ability-сущности в кадре
                        // призыва уже стоит СЛЕДУЮЩИЙ виновник (пачка токенов — FillRowWithCardEffect{MaxCount>1},
                        // RepeatEffect). Перезапускаем полный цикл (rules→targeting→resolve) для него — иначе
                        // «Двойной удар» (и любая другая TriggerSubject-способность) доставался бы только первому
                        // из пачки. AbilityCastEvent уже снят RunCheckAbilityRulesSystem — можно ставить заново.
                        var subjPool = world.GetPool<TriggerSubjectComponent>();
                        if (subjPool.Has(first) && !world.GetPool<AbilityCastEvent>().Has(first))
                        {
                            ref var c2 = ref world.GetPool<AbilityCastEvent>().Add(first);
                            c2.CardEntity = caster;
                            c2.OwnerPlayerEntity = playerEntity;
                        }
                    }
                }
            }

            // КОСМЕТИКА Beam/Area (Projectile уже запущен в LaunchProjectile) + универсальный индикатор
            // OnCast/OnDie (см. EmitDefaultTriggerVfx) — играет ВСЕГДА для этих двух триггеров, ДОПОЛНИТЕЛЬНО
            // к своему VFX способности, а не вместо него (решение пользователя — как в HS: «черепок» на
            // деатрэттле и «белая аура» на баттлкрае идут поверх ЛЮБОГО кастомного эффекта карты, не только
            // когда у карты своего VFX нет).
            var vfxPool = world.GetPool<AbilityVfxComponent>();
            var vfxSpec = vfxPool.Has(first) ? vfxPool.Get(first).Spec : null;
            bool hasCustomVisual = vfxSpec != null && (vfxSpec.HitPrefab != null || (vfxSpec.Kind != VfxKind.None && vfxSpec.Prefab != null));

            if (_boardView.Value != null)
            {
                if (hasCustomVisual && targets.Length > 0)
                {
                    // Field-способность (AbilityToField «по всем врагам/своим» и т.п.) — Area-VFX красит ЗОНУ
                    // (половину/всё поле), а не баунды фактических целей (см. VfxEmitUtil.ZoneBounds — иначе
                    // единственная задетая цель схлопывает эффект в точку вместо «всей стороны», как в HS).
                    var fieldPool = world.GetPool<AbilityFieldComponent>();
                    Bounds? zone = fieldPool.Has(first)
                        ? VfxEmitUtil.ZoneBounds(world, _boardView.Value, fieldPool.Get(first).Area, caster, playerEntity)
                        : null;
                    EmitVfx(world, vfxSpec, caster, targets, zone);
                }
                EmitDefaultTriggerVfx(world, caster, triggerKey, isSelfTrigger);
            }

            int[] summoned = SummonScratch.Summoned.Count > 0 ? SummonScratch.Summoned.ToArray() : Array.Empty<int>();

            // Роллы недетерм. генерации → в снапшот; чистим replay.
            string[] genExps = null; int[] genIds = null;
            if (GeneratedCardChannel.Sent.Count > 0)
            {
                int gc = GeneratedCardChannel.Sent.Count;
                genExps = new string[gc]; genIds = new int[gc];
                for (int i = 0; i < gc; i++) { genExps[i] = GeneratedCardChannel.Sent[i].exp; genIds[i] = GeneratedCardChannel.Sent[i].cardId; }
            }
            GeneratedCardChannel.ClearReplay();
            GeneratedCardChannel.ClearSent();

            // Синк: коллектор (актив пошлёт ActionAbilityData; пассив — резолв впрыснутой очереди, эхо отфильтруется).
            GameEventBus.Publish(new AbilityResolvedNetEvent
            {
                SourceCardEntity      = caster,
                AbilityIndex          = abilityIndex,
                TargetEntities        = targets,
                SummonedEntities      = summoned,
                GeneratedExpansionIds = genExps,
                GeneratedCardIds      = genIds,
            });

            _nextResolveAt = Time.time + ActionPacing.GapSeconds;   // пауза перед следующей способностью очереди
        }

        // Универсальный индикатор триггера (как в HS: «черепок» на деатрэттле, «белая аура» на баттлкрае) —
        // вспышка НА КАСТЕРЕ для OnCast/OnDie из DefaultAbilityVfxConfig. Играет ВСЕГДА при этих триггерах,
        // ДОПОЛНИТЕЛЬНО к любому кастомному VFX способности (не фолбэк — см. вызывающий код). triggerKey/
        // isSelfTrigger приходят параметрами (не читаем AbilityResolveContext здесь — к этому моменту
        // ResolveAbility уже вызвал AbilityResolveContext.Clear() в своём finally; вызывающий передаёт
        // локально захваченные копии до Clear()). Строки совпадают с TriggerKeys.OnCast/OnDie в
        // Game.Core.Ability, но тот класс internal и в другой сборке — сверяем литералом.
        //
        // isSelfTrigger ОБЯЗАТЕЛЕН: Key="OnCast" сам по себе НЕ означает «это МОЙ каст» — реактивные триггеры
        // (OnOwnerCardPlayedTrigger: Блаженный дьякон/Упс/Королевский палач — «когда вы разыгрываете ЛЮБУЮ
        // карту») тоже шлют Key="OnCast" ради множителя CastMultiplierService, реагируя при этом на ЧУЖОЙ
        // каст. Без этой проверки вспышка играла бы на реактивной карте КАЖДЫЙ РАЗ, когда игрок кастует
        // что угодно другое — баг 2026-08-11: «эффект каста на аватаре, а на деле на другом существе».
        // Любой другой триггер (OnAttack/OnTurnStart/…) индикатор не получает — только эти два, и только
        // когда они реально про себя.
        void EmitDefaultTriggerVfx(EcsWorld world, int caster, string triggerKey, bool isSelfTrigger)
        {
            if (!isSelfTrigger) return;

            var cfg = _defaultVfx.Value;
            if (cfg == null) return;

            GameObject prefab = triggerKey switch
            {
                "OnCast" => cfg.OnCastVfxPrefab,
                "OnDie"  => cfg.OnDieVfxPrefab,
                _        => null,
            };
            if (prefab == null) return;

            GameEventBus.Publish(new HitVfxEvent { At = WorldPos(world, caster), Prefab = prefab });
        }

        // Публикует косметику Beam/Area (Projectile запускается в LaunchProjectile с токеном ожидания).
        void EmitVfx(EcsWorld world, VfxSpec spec, int caster, int[] targets, Bounds? zoneBounds = null)
            => VfxEmitUtil.EmitInstantVfx(world, _boardView.Value, spec, caster, targets, zoneBounds);

        // Мировая позиция сущности для VFX (см. VfxEmitUtil.WorldPos — общая логика).
        Vector3 WorldPos(EcsWorld world, int entity) => VfxEmitUtil.WorldPos(world, _boardView.Value, entity);
    }
}
