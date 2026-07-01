using System;
using System.Collections.Generic;
using Leopotam.EcsLite;
using UnityEngine;
using Game.Core.Shared.Interface;
using Game.Core.Ecs.Components;

namespace Game.Core.Ability
{
    // === class (OOP) ===
    /// <summary>
    /// Способность-носитель. Authored как данные ([SerializeReference]-списки), на ините
    /// КЛОНИРУЕТСЯ под каждую сущность карты (DeepClone) и становится рантайм-инстансом.
    ///
    /// Жизненный цикл привязан к сущности карты:
    ///   CardModel.Init() -> создать ability-сущность -> ability.DeepClone() -> ability.Init(...)
    ///   уничтожение карты -> ability.Dispose()
    /// Зона (борд/рука) НЕ влияет на подписку — её проверяют правила в момент срабатывания.
    /// </summary>
    [Serializable]
    public abstract class Ability : IAbility
    {
        [SerializeReference] public List<ITrigger> Triggers = new();
        public List<Rule> Rules = new();   // группы правил; на ините → AbilityRuleContainerComponent на сущности
        [SerializeReference] public List<IEffect> Effects = new();

        // Косметика каста (луч/снаряд/область). Авторится на карте, на game-state НЕ влияет.
        public VfxSpec Vfx;

        // ── lifecycle ────────────────────────────────────────────────────────
        public void Init(EcsWorld world, int abilityEntity, int cardEntity, int playerEntity, int abilityIndex)
        {
            // Обратная ссылка на владельцев (карта/игрок/индекс) — нужна резолв-системе и синку.
            ref var owner = ref world.GetPool<AbilityOwnerComponent>().Add(abilityEntity);
            owner.CardEntity   = cardEntity;
            owner.PlayerEntity = playerEntity;
            owner.AbilityIndex = abilityIndex;

            // Контейнер правил — прогоняет RunCheckAbilityRulesSystem.
            if (Rules != null && Rules.Count > 0)
                world.GetPool<AbilityRuleContainerComponent>().Add(abilityEntity).Rules = Rules.ToArray();

            // Контейнер эффектов — применяет RunResolveAbilityQueueSystem. Здесь же инитим условия эффектов.
            if (Effects != null && Effects.Count > 0)
            {
                foreach (var effect in Effects) effect.Init(world, cardEntity, playerEntity);
                world.GetPool<AbilityEffectContainerComponent>().Add(abilityEntity).Effects = Effects.ToArray();
            }

            // Контейнер триггеров — они сами подписываются на шину в Init.
            if (Triggers != null && Triggers.Count > 0)
            {
                foreach (var trigger in Triggers)
                    trigger.Init(world, abilityEntity, cardEntity, playerEntity);
                world.GetPool<AbilityTriggerContainerComponent>().Add(abilityEntity).Triggers = Triggers.ToArray();
            }

            // Косметическая VFX-спека (если задана) — резолв-система прочитает и опубликует событие.
            if (Vfx != null && Vfx.Kind != VfxKind.None && Vfx.Prefab != null)
                world.GetPool<AbilityVfxComponent>().Add(abilityEntity).Spec = Vfx;

            OnInit(world, abilityEntity);
        }

        public void Dispose()
        {
            foreach (var trigger in Triggers) trigger.Dispose();
            if (Effects != null) foreach (var effect in Effects) effect.Dispose();
            OnDispose();
        }

        // ── extension points ─────────────────────────────────────────────────
        // OnInit: подтип пишет свой таргетинг-компонент (Target/Field) на ability-сущность.
        protected virtual void OnInit(EcsWorld world, int abilityEntity) { }
        protected virtual void OnDispose() { }

        /// <summary>Глубокая копия шаблона (триггеры/правила/эффекты), чтобы у каждой сущности был свой стейт.</summary>
        public virtual Ability DeepClone() => AbilityCloneUtil.DeepClone(this);
    }

    // === class (OOP) === три подтипа: пишут свой таргетинг-компонент в OnInit.

    /// <summary>Цель(и): Random/Selected + фильтры валидных целей.</summary>
    public sealed class AbilityToTarget : Ability
    {
        public TargetSelection Selection = TargetSelection.Selected;
        public int Count = 1;
        public TargetZone Zone = TargetZone.Board;

        // Только для Selected из не-Board зон (discover-окно показывает поднабор): сколько показать (0 = все).
        // Board (клики) и Random (auto-pick) его игнорируют.
        public int OfferCount = 3;
        [SerializeReference] public List<ITargetFilter> Filters = new();

        protected override void OnInit(EcsWorld world, int abilityEntity)
        {
            ref var t = ref world.GetPool<AbilityTargetComponent>().Add(abilityEntity);
            t.Selection = Selection;
            t.Count = Count;
            t.OfferCount = OfferCount;
            t.Zone = Zone;
            t.Filters = Filters != null ? Filters.ToArray() : Array.Empty<ITargetFilter>();
        }
    }

    /// <summary>По области (всё/своя/вражеская) + зоне + фильтры на кого воздействуем.</summary>
    public sealed class AbilityToField : Ability
    {
        public FieldArea Area = FieldArea.All;
        public TargetZone Zone = TargetZone.Board;
        [SerializeReference] public List<ITargetFilter> Filters = new();

        protected override void OnInit(EcsWorld world, int abilityEntity)
        {
            ref var f = ref world.GetPool<AbilityFieldComponent>().Add(abilityEntity);
            f.Area = Area;
            f.Zone = Zone;
            f.Filters = Filters != null ? Filters.ToArray() : Array.Empty<ITargetFilter>();
        }
    }

    /// <summary>Без цели — этап таргетинга пропускается (targets = [player]).</summary>
    public sealed class AbilityNonTarget : Ability
    {
    }

    /// <summary>Цель — САМ источник (caster cardEntity). Для самобаффов/самолечения:
    /// напр. OnTurnStart + AbilityToSelf + BuffStatsEffect{SpeedBonus=1} (Боец на арене).</summary>
    public sealed class AbilityToSelf : Ability
    {
        protected override void OnInit(EcsWorld world, int abilityEntity)
            => world.GetPool<AbilitySelfComponent>().Add(abilityEntity);
    }
}
