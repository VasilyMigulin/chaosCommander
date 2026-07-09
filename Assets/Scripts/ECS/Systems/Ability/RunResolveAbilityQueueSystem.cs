using System;
using System.Collections.Generic;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Mono;
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
        readonly EcsPoolInject<ViewRefComponent> _viewPool = default;

        const float ProjectileTimeout = 4f;   // сек до форс-резолва, если прилёт не пришёл

        readonly Queue<int> _arrived = new Queue<int>();          // токены приземлившихся снарядов (ability-сущности)
        readonly Queue<int> _castPointReached = new Queue<int>(); // CastEvent анимации кастера (ability-сущности)
        readonly Queue<int> _animFinished = new Queue<int>();     // FinishEvent анимации кастера (ability-сущности)
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
            var pendingPool = world.GetPool<AbilityCastPendingComponent>();
            var animPendingPool = world.GetPool<AbilityAnimPendingComponent>();

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

            // 5) Обычный разбор очереди — одна способность за тик.
            var queuedPool = world.GetPool<AbilityQueuedState>();
            var ownerPool  = world.GetPool<AbilityOwnerComponent>();

            int first = -1;
            foreach (var entity in world.Filter<AbilityQueuedState>().Inc<AbilityOwnerComponent>().End())
            { first = entity; break; }
            if (first < 0) return;

            // Анимация кастера (opt-in): если запрошена И у кастера реально есть параметр "Cast" —
            // откладываем резолв до CastEvent/FinishEvent. Иначе — мгновенно, как раньше.
            var vfxPoolCheck = world.GetPool<AbilityVfxComponent>();
            if (vfxPoolCheck.Has(first) && vfxPoolCheck.Get(first).Spec?.PlayCasterAnimation == true)
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

        // Запуск снаряда: публикуем ProjectileVfxEvent на каждую цель, вешаем pending (гейт) + блок инпута.
        // Эффекты НЕ применяем и AbilityQueuedState НЕ снимаем — ждём прилёта.
        void LaunchProjectile(EcsWorld world, int ability, int caster, int[] targets, VfxSpec spec)
        {
            Vector3 from = WorldPos(world, caster);
            foreach (var t in targets)
                GameEventBus.Publish(new ProjectileVfxEvent
                {
                    From = from, To = WorldPos(world, t),
                    Prefab = spec.Prefab, HitPrefab = spec.HitPrefab,
                    Speed = spec.ProjectileSpeed, Token = ability,
                });

            ref var pending = ref world.GetPool<AbilityCastPendingComponent>().Add(ability);
            pending.Deadline = Time.time + ProjectileTimeout;
            GameEventBus.Publish(new InputBlockedEvent());
            Debug.Log($"[Resolve] projectile launched ability={ability} targets={targets.Length} (ждём хит)");
        }

        // Прилёт снаряда (или форс по таймауту): снимаем pending, разблокируем инпут, применяем эффекты.
        void LandAndResolve(EcsWorld world, int ability)
        {
            world.GetPool<AbilityCastPendingComponent>().Del(ability);
            GameEventBus.Publish(new InputRestoredEvent());
            ResolveAbility(world, ability);
        }

        // Применение эффектов + сбор синка + снапшот + косметика (Beam/Area). Вызывается мгновенно
        // (Beam/Area/без-VFX) или на прилёте снаряда (Projectile).
        void ResolveAbility(EcsWorld world, int first)
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

            Debug.Log($"[Resolve] ability={first} card={caster} abilityIdx={abilityIndex} targets={targets.Length}");

            // Скрэтч призванных за этот резолв (для синка модификаторов призыва).
            SummonScratch.Clear();

            // Инициатор резолва (атрибуция генерации «кем замешано»).
            var originPool = world.GetPool<AbilityOriginComponent>();
            AbilityResolveContext.OriginOwnerId = originPool.Has(first) ? originPool.Get(first).OriginOwnerId : -1;

            // Ключ триггера ЭТОЙ активации (PlayCardFromZoneEffect/PlaySameNameFromHandEffect: интерактивный
            // выбор цели только от OnCast — см. AbilityResolveContext.TriggerKey).
            var triggerKeyPool = world.GetPool<AbilityTriggerKeyComponent>();
            AbilityResolveContext.TriggerKey = triggerKeyPool.Has(first) ? triggerKeyPool.Get(first).Key : null;

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
                    if (queuedPool.Has(first)) queuedPool.Del(first);
                }
            }

            // КОСМЕТИКА Beam/Area (Projectile уже запущен в LaunchProjectile).
            var vfxPool = world.GetPool<AbilityVfxComponent>();
            if (_boardView.Value != null && vfxPool.Has(first) && targets.Length > 0)
                EmitVfx(world, vfxPool.Get(first).Spec, caster, targets);

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
        }

        // Публикует косметику Beam/Area (Projectile запускается в LaunchProjectile с токеном ожидания).
        void EmitVfx(EcsWorld world, VfxSpec spec, int caster, int[] targets)
        {
            if (spec == null || spec.Prefab == null || spec.Kind == VfxKind.None) return;
            var bv = _boardView.Value;

            switch (spec.Kind)
            {
                case VfxKind.Beam:
                    Vector3 from = WorldPos(world, caster);
                    foreach (var t in targets)
                        GameEventBus.Publish(new BeamVfxEvent
                        { From = from, To = WorldPos(world, t), Prefab = spec.Prefab, HitPrefab = spec.HitPrefab });
                    break;

                case VfxKind.Area:
                    var centers = new Vector3[targets.Length];
                    for (int i = 0; i < targets.Length; i++) centers[i] = WorldPos(world, targets[i]);
                    GameEventBus.Publish(new AreaVfxEvent
                    { CellCenters = centers, CellSize = bv.CellSize, Prefab = spec.Prefab, HitPrefab = spec.HitPrefab, Merge = spec.MergeArea });
                    break;
            }
        }

        // Мировая позиция сущности для VFX: клетка борда → аватар (игрок) → инстанс вью → аватар владельца
        // (спелл из руки) → центр доски (фолбэк).
        Vector3 WorldPos(EcsWorld world, int entity)
        {
            var bv = _boardView.Value;

            var posPool = world.GetPool<BoardPositionComponent>();
            if (posPool.Has(entity))
            {
                ref var p = ref posPool.Get(entity);
                var cell = bv.GetCell(p.Row, p.Col, p.OwnerId);
                if (cell != null) return cell.transform.position;
            }

            var playerPool = world.GetPool<PlayerComponent>();
            if (playerPool.Has(entity))
            {
                var ac = bv.GetAvatarCell(playerPool.Get(entity).PlayerId);
                if (ac != null) return ac.transform.position;
            }

            var viewPool = world.GetPool<ViewRefComponent>();
            if (viewPool.Has(entity) && viewPool.Get(entity).View != null)
                return viewPool.Get(entity).View.transform.position;

            var ownerPool = world.GetPool<OwnerComponent>();
            if (ownerPool.Has(entity))
            {
                var ac = bv.GetAvatarCell(ownerPool.Get(entity).OwnerId);
                if (ac != null) return ac.transform.position;
            }

            return bv.BoardCenter;
        }
    }
}
