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
    public abstract class Ability
    {
        public EnumService.AbilityTrigger Trigger;
        public EnumService.AbilityTarget Target;

        public List<AbilityEffect> Effects = new List<AbilityEffect>();
        public List<AbilityCondition> Conditions = new List<AbilityCondition>();

        public int Init(EcsWorld world, int entityCard)
        {
            int entity = world.NewEntity();

            AddCondition(world, entity);

            AddEffect(world, entity);

            AddTrigger(world, entity);

            AddTarget(world, entity);

            OnInit(world, entity, entityCard);

            return entity;
        }

        protected abstract void OnInit(EcsWorld world, int entity, int entityCard);

        void AddCondition(EcsWorld world, int entity)
        {
            ref var conditionContainerComp = ref world.GetPool<AbilityConditionContainerComponent>().Add(entity);
            conditionContainerComp.AbilityConditions = new List<IAbilityCondition>();

            foreach (var condition in Conditions)
            {
                conditionContainerComp.AbilityConditions.Add(condition.Clone());
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
            foreach (EnumService.AbilityTarget flag in System.Enum.GetValues(typeof(EnumService.AbilityTarget)))
            {
                if (flag == EnumService.AbilityTarget.None) continue;
                if ((Target & flag) == 0) continue;

                switch (flag)
                {
                    case EnumService.AbilityTarget.Self:
                        world.GetPool<TargetTag>().Add(entityAbility);
                        break;
                    case EnumService.AbilityTarget.Enemy:
                        world.GetPool<TargetTag>().Add(entityAbility);
                        break;
                    case EnumService.AbilityTarget.Ally:
                        world.GetPool<TargetTag>().Add(entityAbility);
                        break;
                    case EnumService.AbilityTarget.Player:
                        world.GetPool<TargetTag>().Add(entityAbility);
                        break;
                }
            }
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
                }
            }
        }
    }
}