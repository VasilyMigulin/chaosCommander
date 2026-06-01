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

        // ── Единый блок нацеливания ──────────────────────────────────────────
        /// <summary>Маска — кто/что годится в цель (Self / AllyCreature / EnemyPlayer / All / ...).</summary>
        public TargetMask Target;

        /// <summary>Как выбирается цель: Auto (сама), Select (игрок кликает), Random (случайная).</summary>
        public TargetingMode Mode = TargetingMode.Auto;

        /// <summary>Форма области вокруг выбранной цели (Single — только сама цель).</summary>
        public TargetShape Shape = TargetShape.Single;

        [SerializeReference] public List<AbilityEffect> Effects = new List<AbilityEffect>();
        [SerializeReference] public List<AbilityCondition> Conditions = new List<AbilityCondition>();

        /// <summary>
        /// Цепочка шагов после основных Effects. Каждый шаг применяется только
        /// когда предыдущий полностью отработал. Цель шага определяется его
        /// ChainStep.TargetSource (например, выбранная раскопкой карта или
        /// сохранённая клетка убитой цели).
        /// </summary>
        [SerializeReference] public List<ChainStep> ChainSteps = new List<ChainStep>();

        /// <summary>
        /// Срок действия ауры в ходах ВЛАДЕЛЬЦА. 0 = постоянная. Применяется
        /// только для Trigger == Aura — Init вешает DurationAuraComponent на саму
        /// карту-чару; DurationAuraTickSystem уменьшает счётчик в начале хода и
        /// убирает чару с поля при достижении 0.
        /// </summary>
        public int AuraDurationTurns;

        /// <summary>
        /// Реакции ауры на игровые события (Вуду-будду / Дерзкий расхититель и т.п.).
        /// Висят на чаре пока та на доске. См. AuraReactionDispatcherSystem.
        /// </summary>
        [SerializeReference] public List<AuraReaction> AuraReactions = new List<AuraReaction>();

        /// <summary>Требование к цветам цели: хотя бы один (AnyRequired=true) / все эти цвета.</summary>
        public EnumService.Element RequiredColors;

        /// <summary>Запрещённые цвета цели (хотя бы один из этих цветов → отсев).</summary>
        public EnumService.Element ForbiddenColors;

        /// <summary>Если true — достаточно одного цвета из Required; иначе нужны ВСЕ.</summary>
        public bool RequiredColorsAny = true;

        /// <summary>
        /// При TargetMask.Random — сколько РАЗНЫХ случайных целей выбрать. 0/1 = одна (старое поведение).
        /// Используется «Цепная молния» (3 случайных врага).
        /// </summary>
        public int RandomTargetCount = 1;

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

            AddChainSteps(world, entity);

            AddTrigger(world, entity);

            AddTarget(world, entity, entityCard);

            AddColorRequirement(world, entity);

            AddRandomTargetCount(world, entity);

            AddAura(world, entity, entityCard);

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

        void AddChainSteps(EcsWorld world, int entityAbility)
        {
            if (ChainSteps == null || ChainSteps.Count == 0) return;

            var clones = new List<Game.Core.Shared.Interface.IChainStep>(ChainSteps.Count);
            foreach (var step in ChainSteps)
            {
                if (step == null) continue;
                var stepClone = new ChainStep
                {
                    TargetSource       = step.TargetSource,
                    Shape              = step.Shape,
                    TargetMaskOverride = step.TargetMaskOverride,
                    Condition          = step.Condition,
                    Effects            = new List<AbilityEffect>(step.Effects?.Count ?? 0),
                };
                if (step.Effects != null)
                {
                    foreach (var eff in step.Effects)
                    {
                        if (eff == null) continue;
                        stepClone.Effects.Add((AbilityEffect)eff.Clone());
                    }
                }
                clones.Add(stepClone);
            }

            ref var container = ref world.GetPool<AbilityChainContainerComponent>().Add(entityAbility);
            container.Steps = clones;
        }

        void AddTarget(EcsWorld world, int entityAbility, int entityCard)
        {
            // 1. Маска эффекта и форма — на entity способности
            if (Target != TargetMask.None)
            {
                world.GetPool<TargetMaskComponent>().Add(entityAbility).Mask = Target;

                // All — затронуть всех подходящих (полевой эффект)
                if (Target.Has(TargetMask.All))
                    world.GetPool<FieldAbilityTag>().Add(entityAbility);
            }

            if (Shape != TargetShape.Single)
                world.GetPool<TargetShapeComponent>().Add(entityAbility).Shape = Shape;

            // 2. Режим Auto — других тегов не нужно: система сама найдёт цель.
            if (Mode == TargetingMode.Auto) return;

            // 3. Select / Random — гейтят каст до выбора цели. Теги на КАРТЕ
            //    (CardInputSystem / RandomTargetSystem / CastCardSystem их ждут).
            if (!world.GetPool<TargetRequirementComponent>().Has(entityCard))
                world.GetPool<TargetRequirementComponent>().Add(entityCard).RequiredTarget = Target;

            if (Mode == TargetingMode.Select)
            {
                var p = world.GetPool<RequiresTargetSelectionTag>();
                if (!p.Has(entityCard)) p.Add(entityCard);
            }
            else // Random
            {
                var p = world.GetPool<RequiresRandomTargetTag>();
                if (!p.Has(entityCard)) p.Add(entityCard);
            }

            // 4. Авто-гейт PlayableTag: карта недоступна, если на поле нет валидных целей.
            var reqPool = world.GetPool<AbilityPlayRequirementContainerComponent>();
            if (!reqPool.Has(entityCard))
            {
                ref var c = ref reqPool.Add(entityCard);
                c.PlayRequirements = new List<IAbilityPlayRequirement>();
            }
            ref var reqContainer = ref reqPool.Get(entityCard);
            if (reqContainer.PlayRequirements == null)
                reqContainer.PlayRequirements = new List<IAbilityPlayRequirement>();
            reqContainer.PlayRequirements.Add(
                new Game.Core.Model.Condition.RequireValidTargetPlayRequirement(Target));
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

        void AddColorRequirement(EcsWorld world, int entityAbility)
        {
            if (RequiredColors == 0 && ForbiddenColors == 0) return;
            ref var c = ref world.GetPool<ColorRequirementComponent>().Add(entityAbility);
            c.Required = RequiredColors;
            c.Forbidden = ForbiddenColors;
            c.AnyRequired = RequiredColorsAny;
        }

        void AddRandomTargetCount(EcsWorld world, int entityAbility)
        {
            if (RandomTargetCount <= 1) return;
            ref var c = ref world.GetPool<RandomTargetCountComponent>().Add(entityAbility);
            c.Count = RandomTargetCount;
        }

        void AddAura(EcsWorld world, int entityAbility, int entityCard)
        {
            if ((Trigger & EnumService.AbilityTrigger.Aura) == 0) return;

            int attackBonus = 0;
            int healthBonus = 0;
            int speedBonus  = 0;

            foreach (var effect in Effects)
            {
                if (effect is BuffStatsEffect buff)
                {
                    attackBonus += buff.AttackBonus;
                    healthBonus += buff.HealthBonus;
                    speedBonus  += buff.SpeedBonus;
                }
            }

            ref var aura = ref world.GetPool<AuraSourceComponent>().Add(entityAbility);
            aura.AttackBonus = attackBonus;
            aura.HealthBonus = healthBonus;
            aura.SpeedBonus  = speedBonus;

            // Аура с лимитом по ходам — на чару вешается DurationAuraComponent.
            // При множественных аура-абилках на карте берётся максимум значения.
            if (AuraDurationTurns > 0 && entityCard >= 0)
            {
                var durationPool = world.GetPool<DurationAuraComponent>();
                if (!durationPool.Has(entityCard))
                {
                    ref var d = ref durationPool.Add(entityCard);
                    d.TurnsRemaining = AuraDurationTurns;
                }
                else if (durationPool.Get(entityCard).TurnsRemaining < AuraDurationTurns)
                {
                    durationPool.Get(entityCard).TurnsRemaining = AuraDurationTurns;
                }
            }

            // Реакции на события: накапливаем в одном контейнере на карте,
            // чтобы дать чаре сразу несколько реакций (или совмещать с другими аура-абилками).
            if (AuraReactions != null && AuraReactions.Count > 0 && entityCard >= 0)
            {
                var reactionPool = world.GetPool<AuraReactionContainerComponent>();
                if (!reactionPool.Has(entityCard))
                {
                    ref var rc = ref reactionPool.Add(entityCard);
                    rc.Reactions = new List<Game.Core.Shared.Interface.IAuraReaction>();
                }
                ref var container = ref reactionPool.Get(entityCard);
                if (container.Reactions == null)
                    container.Reactions = new List<Game.Core.Shared.Interface.IAuraReaction>();
                foreach (var reaction in AuraReactions)
                {
                    if (reaction == null) continue;
                    container.Reactions.Add(reaction);
                }
            }
        }

        // Только не-таргетные требования: раскопка и любые пользовательские условия поля.
        // Выбор цели (Select / Random) задаётся через Mode и вешается в AddTarget.
        void AddPlayRequirement(EcsWorld world, int entityCard)
        {
            if (PlayRequirements == null || PlayRequirements.Count == 0) return;

            var pool = world.GetPool<AbilityPlayRequirementContainerComponent>();
            if (!pool.Has(entityCard))
            {
                ref var c = ref pool.Add(entityCard);
                c.PlayRequirements = new List<IAbilityPlayRequirement>();
            }
            ref var reqContainer = ref pool.Get(entityCard);
            if (reqContainer.PlayRequirements == null)
                reqContainer.PlayRequirements = new List<IAbilityPlayRequirement>();

            foreach (var req in PlayRequirements)
            {
                var clone = req.Clone();
                reqContainer.PlayRequirements.Add(clone);

                // Раскопка (excavate) — вешаем тег и компонент на карту
                if (clone is Game.Core.Model.Condition.RequireCardPickPlayRequirement pickReq)
                    pickReq.ApplyToCard(world, entityCard);
            }
        }
    }
}