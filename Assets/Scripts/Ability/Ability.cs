using System;
using System.Collections.Generic;
using Leopotam.EcsLite;
using UnityEngine;
using Game.Core.Shared.Interface;
using Game.Core.Ecs.Components;
using Game.Core.Events;

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

        // Отписки от ConditionRoot.Changed (подсветка «условие эффекта готово» — см. WireConditionHighlight).
        readonly List<Action> _conditionUnsubs = new();

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
                WireConditionHighlight(abilityEntity, cardEntity);
            }

            // Контейнер триггеров — они сами подписываются на шину в Init.
            if (Triggers != null && Triggers.Count > 0)
            {
                foreach (var trigger in Triggers)
                    trigger.Init(world, abilityEntity, cardEntity, playerEntity);
                world.GetPool<AbilityTriggerContainerComponent>().Add(abilityEntity).Triggers = Triggers.ToArray();
            }

            // Косметическая VFX-спека (если задана) — резолв-система прочитает и опубликует событие.
            // ВАЖНО: компонент нужен, даже если нет Kind/Prefab — либо включён ТОЛЬКО PlayCasterAnimation
            // (анимация каста без отдельного луча/снаряда/области), либо задан ТОЛЬКО HitPrefab (простая
            // вспышка на цели без луча/области — напр. Голубой волшебник: мана себе, никакой геометрии не
            // нужно). Иначе RunResolveAbilityQueueSystem.Run()/ResolveAbility (гейт `vfxPool.Has(first)`)
            // никогда не увидит спеку → ни анимация каста, ни HitPrefab-вспышка не сработают НИКОГДА, даже
            // если оба поля честно проставлены в инспекторе.
            if (Vfx != null && (Vfx.PlayCasterAnimation || Vfx.HitPrefab != null || (Vfx.Kind != VfxKind.None && Vfx.Prefab != null)))
                world.GetPool<AbilityVfxComponent>().Add(abilityEntity).Spec = Vfx;

            OnInit(world, abilityEntity);
        }

        public void Dispose()
        {
            foreach (var unsub in _conditionUnsubs) unsub();
            _conditionUnsubs.Clear();
            foreach (var trigger in Triggers) trigger.Dispose();
            if (Effects != null) foreach (var effect in Effects) effect.Dispose();
            OnDispose();
        }

        /// <summary>ХС-семантика смены контроля (см. IAbility.Rebind): Dispose (отписка от шины) → очистка
        /// компонентов ability-сущности (Init кладёт их через Add — повторный Init без очистки упал бы) →
        /// Init с НОВЫМ playerEntity. Тот же инстанс → поля триггеров/условий (OnMatchStartTrigger._fired,
        /// накопители) переживают пере-привязку.</summary>
        public void Rebind(EcsWorld world, int abilityEntity, int cardEntity, int newPlayerEntity, int abilityIndex)
        {
            Dispose();
            ClearAbilityEntity(world, abilityEntity);
            Init(world, abilityEntity, cardEntity, newPlayerEntity, abilityIndex);
        }

        // Снять всё, что кладёт Init/OnInit + рантайм-марки срабатываний (протухшая метка со старым
        // владельцем хуже потерянного файра; к моменту пере-привязки каскад осел — см. TempControlRevertSystem).
        static void ClearAbilityEntity(EcsWorld world, int ae)
        {
            Del<AbilityOwnerComponent>(world, ae);
            Del<AbilityRuleContainerComponent>(world, ae);
            Del<AbilityEffectContainerComponent>(world, ae);
            Del<AbilityTriggerContainerComponent>(world, ae);
            Del<AbilityVfxComponent>(world, ae);
            Del<AbilityTargetComponent>(world, ae);
            Del<AbilityFieldComponent>(world, ae);
            Del<AbilitySelfComponent>(world, ae);
            Del<AbilityCastEvent>(world, ae);
            Del<AbilityTriggerKeyComponent>(world, ae);
            Del<TriggerSubjectComponent>(world, ae);
            Del<PendingCastsComponent>(world, ae);
        }

        static void Del<T>(EcsWorld world, int ae) where T : struct
        {
            var pool = world.GetPool<T>();
            if (pool.Has(ae)) pool.Del(ae);
        }

        // Подсветка «условие эффекта готово» (напр. Воображаемые друзья/Все самому: ConditionRoot →
        // NoCreaturesInDeckCondition — «в колоде нет существ»). БЕЗ ЭТОГО AbilityReadyEvent/AbilityNotReadyEvent
        // никогда не публикуются (CardAffordabilitySystem их слушает и шлёт CardAbilityReadyChangedEvent →
        // PlayCardView.SetHighlight(AbilityReady), но публикующей стороны не было вообще) — подсветка молчала
        // на ВСЕХ картах с условием, а не только у этих двух.
        // Реактив: ConditionRoot.Changed дёргается при смене IsReady — публикуем на каждое изменение.
        // Начальный синк: Init эффекта (и первый Recompute условия) идёт ДО того, как эта подписка вообще
        // повешена (и обычно ДО того, как карта попала в руку — способность инитится сразу на создании карты,
        // зона роли не играет) — поэтому досылаем текущее состояние по CardPlacedInHandViewEvent (карта
        // реально показалась в руке → у неё уже есть PlayCardView-подписчик).
        void WireConditionHighlight(int abilityEntity, int cardEntity)
        {
            bool any = false;
            foreach (var effect in Effects)
            {
                if (effect is not EffectBase eb || eb.ConditionRoot == null) continue;
                any = true;
                var effectRef = eb;
                void Publish()
                {
                    if (effectRef.IsReady) GameEventBus.Publish(new AbilityReadyEvent { AbilityEntity = abilityEntity, CardEntity = cardEntity });
                    else                   GameEventBus.Publish(new AbilityNotReadyEvent { AbilityEntity = abilityEntity, CardEntity = cardEntity });
                }
                effectRef.ConditionRoot.Changed += Publish;
                _conditionUnsubs.Add(() => effectRef.ConditionRoot.Changed -= Publish);
            }
            if (!any) return;

            GameEventBus.Subscribe<CardPlacedInHandViewEvent>(this, e =>
            {
                if (e.CardEntity != cardEntity) return;
                bool ready = false;
                foreach (var effect in Effects)
                    if (effect is EffectBase eb2 && eb2.ConditionRoot != null && eb2.IsReady) { ready = true; break; }
                if (ready) GameEventBus.Publish(new AbilityReadyEvent { AbilityEntity = abilityEntity, CardEntity = cardEntity });
                else       GameEventBus.Publish(new AbilityNotReadyEvent { AbilityEntity = abilityEntity, CardEntity = cardEntity });
            });
            _conditionUnsubs.Add(() => GameEventBus.UnsubscribeAll(this));
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
