using UnityEngine;
using Game.Core.Service;
using Leopotam.EcsLite;
using Game.Core.Ecs.Components;
using System.Collections.Generic;
using Game.Core.Model.Effect;
using Game.Core.Shared.Interface;
using Game.Core.Model.Condition;

namespace Game.Core.Model.Ability
{
    [System.Serializable]
    public abstract class Ability
    {
        public EnumService.AbilityTrigger Trigger;
        public EnumService.AbilityTarget Target;

        [SerializeReference] public List<AbilityEffect> Effects = new List<AbilityEffect>();
        [SerializeReference] public List<AbilityCondition> Conditions = new List<AbilityCondition>();

        /// <summary>
        /// Требования к полю боя, необходимые для того, чтобы карта вообще могла
        /// быть разыграна. Проверяются до разыгрывания, независимо от стоимости.
        /// Например: «на поле оппонента есть существо», «есть существо с чёрным цветом».
        /// </summary>
        [SerializeReference] public List<AbilityPlayRequirement> PlayRequirements = new List<AbilityPlayRequirement>();

        public int Init(EcsWorld world, int entityCard)
        {
            int entity = world.NewEntity();

            AddCondition(world, entity, entityCard);

            AddEffect(world, entity);

            AddTrigger(world, entity);

            AddTarget(world, entity);

            AddAura(world, entity);

            AddPlayRequirement(world, entityCard);

            OnInit(world, entity, entityCard);

            return entity;
        }

        protected abstract void OnInit(EcsWorld world, int entity, int entityCard);

        void AddCondition(EcsWorld world, int entity, int entityCard)
        {
            ref var conditionContainerComp = ref world.GetPool<AbilityConditionContainerComponent>().Add(entity);
            conditionContainerComp.AbilityConditions = new List<IAbilityCondition>();

            foreach (var condition in Conditions)
            {
                var clone = condition.Clone();
                conditionContainerComp.AbilityConditions.Add(clone);
                clone.AddCondition(world, entity, entityCard);
            }

            if (conditionContainerComp.AbilityConditions.Count == 0)
            {
                world.GetPool<ReadyTag>().Add(entity);
            }
        }

        void AddEffect(EcsWorld world, int entity)
        {
            ref var effectContainerComp = ref world.GetPool<AbilityEffectContainerComponent>().Add(entity);
            effectContainerComp.AbilityEffects = new List<IAbilityEffect>();

            foreach (var effect in Effects)
            {
                effectContainerComp.AbilityEffects.Add(effect.Clone());   
            } 
        }

        void AddTarget(EcsWorld world, int entityAbility)
        {
            if (Target == EnumService.AbilityTarget.None) return;

            var flags = AbilityTargetFlags.None;

            if ((Target & EnumService.AbilityTarget.Self) != 0)
                flags |= AbilityTargetFlags.Self;

            if ((Target & EnumService.AbilityTarget.EnemyCreature) != 0)
                flags |= AbilityTargetFlags.EnemyCreature;

            if ((Target & EnumService.AbilityTarget.AllyCreature) != 0)
                flags |= AbilityTargetFlags.AllyCreature;

            if ((Target & EnumService.AbilityTarget.AllyPlayer) != 0)
                flags |= AbilityTargetFlags.AllyPlayer;

            if ((Target & EnumService.AbilityTarget.EnemyPlayer) != 0)
                flags |= AbilityTargetFlags.EnemyPlayer;

            if ((Target & EnumService.AbilityTarget.Random) != 0)
                flags |= AbilityTargetFlags.Random;

            if ((Target & EnumService.AbilityTarget.ExcludeSelf) != 0)
                flags |= AbilityTargetFlags.ExcludeSelf;

            ref var targetFlagsComp = ref world.GetPool<AbilityTargetFlagsComponent>().Add(entityAbility);
            targetFlagsComp.Flags = flags;

            if ((Target & EnumService.AbilityTarget.Field) != 0)
                world.GetPool<FieldAbilityTag>().Add(entityAbility);
        }

        void AddTrigger(EcsWorld world, int entityAbility)
        {
            foreach (EnumService.AbilityTrigger flag in System.Enum.GetValues(typeof(EnumService.AbilityTrigger)))
            {
                if (flag == EnumService.AbilityTrigger.None) continue;
                if ((Trigger & flag) == 0) continue;

                switch (flag)
                {
                    case EnumService.AbilityTrigger.OnCast:
                        world.GetPool<OnCastTrigger>().Add(entityAbility);
                        break;
                    case EnumService.AbilityTrigger.OnInvoke:
                        world.GetPool<OnInvokeTrigger>().Add(entityAbility);
                        break;
                    case EnumService.AbilityTrigger.OnDie:
                        world.GetPool<OnDieTrigger>().Add(entityAbility);
                        break;
                    case EnumService.AbilityTrigger.OnAttack:
                        world.GetPool<OnAttackTrigger>().Add(entityAbility);
                        break;
                    case EnumService.AbilityTrigger.TurnStart:
                        world.GetPool<OnTurnStartTrigger>().Add(entityAbility);
                        break;
                    case EnumService.AbilityTrigger.TurnEnd:
                        world.GetPool<OnTurnEndTrigger>().Add(entityAbility);
                        break;
                    case EnumService.AbilityTrigger.OnAllyDeath:
                        world.GetPool<OnAllyDie>().Add(entityAbility);
                        break;
                    case EnumService.AbilityTrigger.OnEnemyDeath:
                        world.GetPool<OnEnemyDie>().Add(entityAbility);
                        break;
                    case EnumService.AbilityTrigger.OnDrawn:
                        world.GetPool<OnDraw>().Add(entityAbility);
                        break;
                    case EnumService.AbilityTrigger.OnMatchStart:
                        world.GetPool<OnMatchStartTrigger>().Add(entityAbility);
                        break;
                }
            }
        }

        void AddAura(EcsWorld world, int entityAbility)
        {
            if ((Trigger & EnumService.AbilityTrigger.Aura) == 0) return;

            int attackBonus = 0;
            int healthBonus = 0;

            foreach (var effect in Effects)
            {
                if (effect is BuffStatsEffect buff)
                {
                    attackBonus += buff.AttackBonus;
                    healthBonus += buff.HealthBonus;
                }
            }

            ref var aura = ref world.GetPool<AuraSourceComponent>().Add(entityAbility);
            aura.AttackBonus = attackBonus;
            aura.HealthBonus = healthBonus;
        }

        void AddPlayRequirement(EcsWorld world, int entityCard)
        {
            if (PlayRequirements == null || PlayRequirements.Count == 0) return;

            ref var reqContainer = ref world.GetPool<AbilityPlayRequirementContainerComponent>().Add(entityCard);
            reqContainer.PlayRequirements = new List<IAbilityPlayRequirement>();

            foreach (var req in PlayRequirements)
            {
                var clone = req.Clone();
                reqContainer.PlayRequirements.Add(clone);

                // Если это требование выбора цели — сразу вешаем теги на карту
                if (clone is Game.Core.Model.Condition.RequireTargetPlayRequirement targetReq)
                    targetReq.ApplyToCard(world, entityCard);

                // Если это требование выбора карты (раскопка) — вешаем тег и компонент
                if (clone is Game.Core.Model.Condition.RequireCardPickPlayRequirement pickReq)
                    pickReq.ApplyToCard(world, entityCard);
            }
        }
    }
}