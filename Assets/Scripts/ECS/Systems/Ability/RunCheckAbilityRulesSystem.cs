using Game.Core.Ecs.Components;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// Правила способностей (играбельность). Ловит ability-сущности с AbilityCastEvent (их повесили
    /// триггеры), прогоняет контейнер правил (AbilityRuleContainerComponent, AND по группам):
    ///   прошли  → AbilityTargetingState (дальше RunAbilityTargetingSystem выбирает цели);
    ///   не прошли → просто снимаем AbilityCastEvent.
    /// cardEntity/playerEntity берём из AbilityCastEvent. Фильтр включает AbilityRefComponent,
    /// поэтому легаси ability-сущности (без него) не задеваются.
    /// </summary>
    public sealed class RunCheckAbilityRulesSystem : IEcsRunSystem
    {
        public void Run(IEcsSystems systems)
        {
            var world = systems.GetWorld();
            var castPool = world.GetPool<AbilityCastEvent>();
            var rulePool = world.GetPool<AbilityRuleContainerComponent>();
            var targetingPool = world.GetPool<AbilityTargetingState>();
            var chainPool = world.GetPool<AbilityChainComponent>();
            var chainStatePool = world.GetPool<ChainStateComponent>();

            var filter = world.Filter<AbilityCastEvent>().Inc<AbilityRefComponent>().End();

            foreach (var entity in filter)
            {
                ref var cast = ref castPool.Get(entity);

                if (RulesPass(world, rulePool, entity, cast.CardEntity, cast.OwnerPlayerEntity))
                {
                    // Составная (цепочечная) способность → стартуем цепочку (RunChainSystem),
                    // обычная → обычный этап таргетинга.
                    if (chainPool.Has(entity))
                    {
                        if (!chainStatePool.Has(entity)) chainStatePool.Add(entity);
                    }
                    else if (!targetingPool.Has(entity))
                    {
                        targetingPool.Add(entity);
                    }
                }

                castPool.Del(entity);
            }
        }

        static bool RulesPass(EcsWorld world, EcsPool<AbilityRuleContainerComponent> rulePool,
                              int abilityEntity, int cardEntity, int playerEntity)
        {
            if (!rulePool.Has(abilityEntity)) return true;

            var groups = rulePool.Get(abilityEntity).Rules;
            if (groups == null) return true;

            foreach (var group in groups)
                if (group != null && !group.Evaluate(world, cardEntity, playerEntity))
                    return false;

            return true;
        }
    }
}
