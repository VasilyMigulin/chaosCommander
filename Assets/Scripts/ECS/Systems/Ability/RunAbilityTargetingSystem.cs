using System;
using System.Collections.Generic;
using Game.Core.Ecs.Components;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;
using UnityEngine;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// Этап таргетинга. Берёт ability-сущности с AbilityTargetingState и вычисляет цели по типу:
    ///   NonTarget (нет таргет-компонента) → [player];
    ///   Field (AbilityFieldComponent)     → все кандидаты в области по фильтрам;
    ///   Target/Random (AbilityTargetComponent) → Count случайных из валидных;
    ///   Target/Selected                   → переход в AbilityTargetPendingState (ждём игрока).
    /// Результат (кроме Selected) → AbilityQueuedState{Targets}; снимает AbilityTargetingState.
    /// </summary>
    public sealed class RunAbilityTargetingSystem : IEcsRunSystem
    {
        public void Run(IEcsSystems systems)
        {
            var world = systems.GetWorld();
            var statePool = world.GetPool<AbilityTargetingState>();
            var ownerPool = world.GetPool<AbilityOwnerComponent>();
            var targetPool = world.GetPool<AbilityTargetComponent>();
            var fieldPool = world.GetPool<AbilityFieldComponent>();
            var selfPool = world.GetPool<AbilitySelfComponent>();
            var pendingPool = world.GetPool<AbilityTargetPendingState>();
            var pickPendingPool = world.GetPool<AbilityPickPendingState>();
            var queuedPool = world.GetPool<AbilityQueuedState>();

            var filter = world.Filter<AbilityTargetingState>().Inc<AbilityOwnerComponent>().End();

            var pending = new List<int>();
            foreach (var e in filter) pending.Add(e);   // буфер: меняем компоненты по ходу

            foreach (var entity in pending)
            {
                ref var owner = ref ownerPool.Get(entity);

                if (targetPool.Has(entity))
                {
                    ref var tc = ref targetPool.Get(entity);
                    // Авто-розыгрыш (Фокус-покус): карта с ForceRandomTargetingComponent НЕ передаёт выбор игроку —
                    // Selected трактуем как Random (игра сама выбирает цели, как Йогг-Сарон).
                    bool forceRandom = world.GetPool<ForceRandomTargetingComponent>().Has(owner.CardEntity);
                    if (tc.Selection == TargetSelection.Selected && !forceRandom)
                    {
                        // нет валидных целей → способность фуззлится, не зависаем в ожидании выбора
                        var valid = TargetGather.Gather(world, tc.Filters, owner.CardEntity, owner.PlayerEntity, null, tc.Zone);
                        if (valid.Count > 0)
                        {
                            if (tc.Zone == TargetZone.Board)
                            {
                                ref var p = ref pendingPool.Add(entity);   // выбор по клеткам доски
                                p.PlayerEntity = owner.PlayerEntity;
                                p.Chosen = Array.Empty<int>();
                            }
                            else
                            {
                                ref var pp = ref pickPendingPool.Add(entity);   // выбор через окно (колода/рука/кладбище)
                                pp.PlayerEntity = owner.PlayerEntity;
                                pp.Chosen = Array.Empty<int>();
                            }
                        }
                        statePool.Del(entity);
                        continue;
                    }

                    var candidates = TargetGather.Gather(world, tc.Filters, owner.CardEntity, owner.PlayerEntity, null, tc.Zone);
                    Queue(queuedPool, entity, tc.Selection == TargetSelection.Strongest
                        ? PickStrongest(world, candidates, tc.Count)
                        : PickRandom(candidates, tc.Count));
                }
                else if (fieldPool.Has(entity))
                {
                    ref var fc = ref fieldPool.Get(entity);
                    var candidates = TargetGather.Gather(world, fc.Filters, owner.CardEntity, owner.PlayerEntity, fc.Area, fc.Zone);
                    Queue(queuedPool, entity, candidates.ToArray());
                }
                else if (selfPool.Has(entity))
                {
                    Queue(queuedPool, entity, new[] { owner.CardEntity });     // AbilityToSelf → сам источник
                }
                else
                {
                    Queue(queuedPool, entity, new[] { owner.PlayerEntity });   // NonTarget
                }

                statePool.Del(entity);
            }
        }

        static void Queue(EcsPool<AbilityQueuedState> pool, int entity, int[] targets)
        {
            if (!pool.Has(entity)) pool.Add(entity);
            pool.Get(entity).Targets = targets;
        }

        // Count целей с наибольшей атакой (тай-брейк не важен для синка — выбор активного едет в ключах целей).
        static int[] PickStrongest(EcsWorld world, List<int> candidates, int count)
        {
            if (count <= 0 || candidates.Count == 0) return Array.Empty<int>();
            var atk = world.GetPool<AttackComponent>();
            candidates.Sort((a, b) =>
            {
                int va = atk.Has(a) ? atk.Get(a).Value : 0;
                int vb = atk.Has(b) ? atk.Get(b).Value : 0;
                return vb.CompareTo(va);   // по убыванию атаки
            });
            int take = Math.Min(count, candidates.Count);
            var res = new int[take];
            candidates.CopyTo(0, res, 0, take);
            return res;
        }

        static int[] PickRandom(List<int> candidates, int count)
        {
            if (count <= 0 || candidates.Count == 0) return Array.Empty<int>();
            if (candidates.Count <= count) return candidates.ToArray();

            for (int i = 0; i < count; i++)   // частичный Фишер-Йейтс; TODO: детерминизм для синка
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
