using System.Collections.Generic;
using Leopotam.EcsLite;
using UnityEngine;

namespace Game.Core.Shared.Interface
{
    public enum RuleMode { AllOf, AnyOf }

    /// <summary>
    /// Составное правило — группа листьев <see cref="IRule"/>, объединённых по Mode
    /// (AllOf = AND, AnyOf = OR). Это НЕ IRule (IRule — отдельное правило-лист).
    /// Группы лежат в AbilityRuleContainerComponent на ability-сущности; между группами — AND.
    /// </summary>
    [System.Serializable]
    public sealed class Rule
    {
        public RuleMode Mode = RuleMode.AllOf;
        [SerializeReference] public List<IRule> Rules = new();

        public bool Evaluate(EcsWorld world, int cardEntity, int playerEntity)
        {
            if (Rules == null || Rules.Count == 0) return true;

            if (Mode == RuleMode.AllOf)
            {
                foreach (var rule in Rules)
                    if (rule != null && !rule.Evaluate(world, cardEntity, playerEntity))
                        return false;
                return true;
            }

            foreach (var rule in Rules)
                if (rule != null && rule.Evaluate(world, cardEntity, playerEntity))
                    return true;
            return false;
        }
    }
}
