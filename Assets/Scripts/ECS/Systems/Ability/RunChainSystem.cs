using System.Collections.Generic;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;

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
    /// недетерминированная генерация — следующий кусок; сейчас прогон локальный.
    /// </summary>
    public sealed class RunChainSystem : IEcsRunSystem
    {
        public void Run(IEcsSystems systems)
        {
            var world = systems.GetWorld();
            var statePool = world.GetPool<ChainStateComponent>();
            var chainPool = world.GetPool<AbilityChainComponent>();
            var ownerPool = world.GetPool<AbilityOwnerComponent>();

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
                    continue;
                }

                if (!state.Applied)
                {
                    ref var owner = ref ownerPool.Get(entity);
                    int card = owner.CardEntity;
                    int abilityIndex = owner.AbilityIndex;
                    var stage = stages[state.Current];

                    int[] targets = ResolveTargets(world, stage, card, owner.PlayerEntity);

                    ChainContext.CurrentKilled = state.Killed;   // контекст для эффектов стадии
                    GeneratedCardChannel.ClearSent();            // соберём случайные генерации стадии для синка
                    ApplyEffects(world, card, stage?.Effects, targets);

                    // СИНК: снапшот стадии (StepIndex + цели + KilledCount + выбранные случайные карты).
                    PublishStageResolved(card, abilityIndex, state.Current, targets, state.Killed);

                    state.LastTargets = targets;
                    state.Applied = true;
                }
                else
                {
                    if (!WorldSettled(world)) continue;   // ждём, пока урон/смерти прошлой стадии осядут

                    state.Killed += CountDead(world, state.LastTargets);
                    state.Current++;
                    state.Applied = false;
                    state.LastTargets = null;

                    if (state.Current >= stages.Length)
                        statePool.Del(entity);
                }
            }
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
                        default:                             return PickRandom(list, stage.Count);
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

        // ── settle / death count ─────────────────────────────────────────────

        static bool WorldSettled(EcsWorld world)
        {
            if (world.Filter<TakeDamageEvent>().End().GetEntitiesCount() > 0) return false;        // урон не применён
            if (world.Filter<DeadTag>().Inc<BoardTag>().End().GetEntitiesCount() > 0) return false; // смерти не обработаны
            if (world.Filter<MovingTag>().End().GetEntitiesCount() > 0) return false;
            if (world.Filter<AttackAnimPendingTag>().End().GetEntitiesCount() > 0) return false;
            return true;
        }

        static int CountDead(EcsWorld world, int[] targets)
        {
            if (targets == null) return 0;
            var dead = world.GetPool<DeadTag>();
            var hp = world.GetPool<HealthComponent>();
            int n = 0;
            foreach (var t in targets)
            {
                if (dead.Has(t)) { n++; continue; }
                if (hp.Has(t) && hp.Get(t).Current <= 0) n++;
            }
            return n;
        }

        static int[] PickRandom(List<int> candidates, int count)
        {
            if (count <= 0 || candidates.Count == 0) return System.Array.Empty<int>();
            if (candidates.Count <= count) return candidates.ToArray();

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
