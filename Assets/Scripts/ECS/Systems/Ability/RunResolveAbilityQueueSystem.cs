using System;
using System.Collections.Generic;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Mono;
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
    /// ДОСТАВКА: если у способности VfxKind.Projectile — эффекты НЕ применяются сразу: запускаем снаряд,
    /// вешаем AbilityCastPendingComponent (гейт очереди + реплея, как AttackAnimPendingTag) и ждём
    /// VfxArrivedEvent от VfxPresenter (на прилёте). Снапшот (AbilityResolvedNetEvent) шлётся ТОЖЕ на
    /// прилёте — иначе случайные роллы (GeneratedCardChannel) уйдут раньше применения → рассинхрон.
    /// Beam/Area/без-VFX — мгновенно. Анти-софтлок: Deadline форсит резолв, если прилёт не пришёл
    /// (нет VfxPresenter/префаба). Синк: каждый клиент гейтит свой снаряд и применяет на своём прилёте;
    /// порядок действий фиксирован снапшот-очередью → детерминизм сохраняется.
    /// </summary>
    public sealed class RunResolveAbilityQueueSystem : IEcsInitSystem, IEcsRunSystem, IEcsDestroySystem
    {
        readonly EcsCustomInject<BoardView> _boardView = default;

        const float ProjectileTimeout = 4f;   // сек до форс-резолва, если прилёт не пришёл

        readonly Queue<int> _arrived = new Queue<int>();   // токены приземлившихся снарядов (ability-сущности)
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

            // 1) Приземлившиеся снаряды → применить отложенные эффекты.
            while (_arrived.Count > 0)
            {
                int token = _arrived.Dequeue();
                if (token >= 0 && pendingPool.Has(token)) LandAndResolve(world, token);
            }

            // 2) Анти-софтлок: форсим резолв просроченных «в полёте» (нет VfxPresenter/префаба). Один за тик.
            foreach (var e in world.Filter<AbilityCastPendingComponent>().End())
            {
                if (pendingPool.Get(e).Deadline <= Time.time) { LandAndResolve(world, e); break; }
            }

            // 3) Пока что-то в полёте — следующее действие не берём (гейт, как у атаки).
            if (world.Filter<AbilityCastPendingComponent>().End().GetEntitiesCount() > 0) return;

            // 4) Обычный разбор очереди — одна способность за тик.
            var queuedPool = world.GetPool<AbilityQueuedState>();
            var ownerPool  = world.GetPool<AbilityOwnerComponent>();

            int first = -1;
            foreach (var entity in world.Filter<AbilityQueuedState>().Inc<AbilityOwnerComponent>().End())
            { first = entity; break; }
            if (first < 0) return;

            // Снаряд-on-hit? (есть BoardView, спека Projectile, префаб и хотя бы одна цель.)
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

            // Иначе — применяем сразу.
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
            ref var queued = ref queuedPool.Get(first);
            int[] targets = queued.Targets ?? Array.Empty<int>();

            Debug.Log($"[Resolve] ability={first} card={caster} abilityIdx={abilityIndex} targets={targets.Length}");

            // Скрэтч призванных за этот резолв (для синка модификаторов призыва).
            SummonScratch.Clear();

            // Инициатор резолва (атрибуция генерации «кем замешано»).
            var originPool = world.GetPool<AbilityOriginComponent>();
            AbilityResolveContext.OriginOwnerId = originPool.Has(first) ? originPool.Get(first).OriginOwnerId : -1;

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
                        foreach (var target in targets)
                            foreach (var effect in effects)
                                if (effect != null && effect.IsReady)
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
