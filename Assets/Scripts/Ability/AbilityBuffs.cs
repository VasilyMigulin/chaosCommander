using System;
using System.Collections.Generic;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;
using UnityEngine;

namespace Game.Core.Ability
{
    // ─────────────────────────────────────────────────────────────────────────
    // ЕДИНАЯ БУФФ-АБСТРАКЦИЯ. Вместо россыпи похожих эффектов (BuffStats/ApplyTrackedBuff/AddPlayerReflect/
    // DeathTimer) — один `AddBuffEffect{IBuffable Buff, Tracked}`, а ЧТО навешивать выбирается типом баффа:
    //   BuffStat (+ATK/HP/Speed), BuffMarker{IMarker} (компонент-маркер: редирект и пр.), BuffDeathTimer.
    // Tracked=true → аура: идемпотентно, трекинг на источнике, авто-откат при его смерти. Глубже и расширяемо.
    // ─────────────────────────────────────────────────────────────────────────

    // === buff: статы ===
    [Serializable]
    public sealed class BuffStat : IBuffable
    {
        public int Attack = 0, Health = 0, Speed = 0;
        public bool Permanent = false;   // переживает смерть (ModifiersPermanent)

        public void Apply(EcsWorld world, int source, int target)
        {
            if (Attack != 0) { var p = world.GetPool<AttackComponent>(); if (p.Has(target)) p.Get(target).AddModifier(Attack, Permanent); }
            if (Health != 0) { var p = world.GetPool<HealthComponent>(); if (p.Has(target)) { ref var h = ref p.Get(target); h.AddModifier(Health, Permanent); if (Health > 0) h.Current += Health; GameEventBus.Publish(new CreatureHealthChangedEvent { CreatureEntity = target }); } }
            if (Speed  != 0) { var p = world.GetPool<SpeedComponent>();  if (p.Has(target)) p.Get(target).AddModifier(Speed, Permanent); }
        }
        public void Revert(EcsWorld world, int source, int target)
        {
            if (Attack != 0) { var p = world.GetPool<AttackComponent>(); if (p.Has(target)) p.Get(target).RemoveModifier(Attack); }
            if (Health != 0) { var p = world.GetPool<HealthComponent>(); if (p.Has(target)) { p.Get(target).RemoveModifier(Health); GameEventBus.Publish(new CreatureHealthChangedEvent { CreatureEntity = target }); } }
            if (Speed  != 0) { var p = world.GetPool<SpeedComponent>();  if (p.Has(target)) p.Get(target).RemoveModifier(Speed); }
        }
    }

    // === buff: ФИКСАЦИЯ статов ===
    // «Стат равен N, что бы на нём ни висело» (Придворный этикет: скорость всех существ = 0). В отличие от
    // BuffStat это НЕ слагаемое, а ЖЁСТКОЕ значение: пока фикс висит, база и ВСЕ модификаторы игнорируются.
    // Фиксы НАКЛАДЫВАЮТСЯ СТЕКОМ (решение юзера 2026-07-30): последний наложенный — актуальный, снятие
    // возвращает предыдущий, пустой стек — обычный расчёт. Снимается как любой бафф (Revert), в т.ч.
    // авто-откатом ауры при смерти источника (AddBuffEffect{Tracked}) — например когда чара умирает.
    // Флаги-переключатели: фиксируем только те статы, у которых стоит Fix* (0 — валидное значение!).
    [Serializable]
    public sealed class FixStat : IBuffable
    {
        [Tooltip("Фиксировать АТАКУ на значении Attack.")]
        public bool FixAttack = false;
        public int Attack = 0;

        [Tooltip("Фиксировать МАКСИМУМ здоровья на значении Health (текущее клампится сверху).")]
        public bool FixHealth = false;
        public int Health = 1;

        [Tooltip("Фиксировать СКОРОСТЬ на значении Speed (0 = существа не ходят и не атакуют).")]
        public bool FixSpeed = false;
        public int Speed = 0;

        public void Apply(EcsWorld world, int source, int target)
        {
            if (FixAttack) { var p = world.GetPool<AttackComponent>(); if (p.Has(target)) p.Get(target).AddOverride(Attack); }
            if (FixHealth) { var p = world.GetPool<HealthComponent>(); if (p.Has(target)) { p.Get(target).AddOverride(Health); GameEventBus.Publish(new CreatureHealthChangedEvent { CreatureEntity = target }); } }
            if (FixSpeed)  { var p = world.GetPool<SpeedComponent>();  if (p.Has(target)) p.Get(target).AddOverride(Speed); }
        }

        public void Revert(EcsWorld world, int source, int target)
        {
            if (FixAttack) { var p = world.GetPool<AttackComponent>(); if (p.Has(target)) p.Get(target).RemoveOverride(Attack); }
            if (FixHealth) { var p = world.GetPool<HealthComponent>(); if (p.Has(target)) { p.Get(target).RemoveOverride(Health); GameEventBus.Publish(new CreatureHealthChangedEvent { CreatureEntity = target }); } }
            if (FixSpeed)  { var p = world.GetPool<SpeedComponent>();  if (p.Has(target)) p.Get(target).RemoveOverride(Speed); }
        }
    }

    // === buff: стоимость карты ===
    // Полный бафф-пайплайн для цены: Permanent (переживает смерть) / мягкий (чистится DieSystem, снимается
    // Revert'ом ауры), стакается (каждый Apply — отдельный модификатор в стеке кост-компонента). Минус = дешевле,
    // плюс = дороже, ниже 0 эффективная цена не падает. НЕ путать с AddCostModifierEffect (Гиперинфляция) —
    // тот вешает ГЛОБАЛЬНЫЙ модификатор на игрока, этот — на конкретную КАРТУ.
    [Serializable]
    public sealed class BuffCost : IBuffable
    {
        [Tooltip("Сдвиг стоимости: -2 = на 2 дешевле, +1 = на 1 дороже.")]
        public int Delta = -1;
        public bool Permanent = false;

        public void Apply(EcsWorld world, int source, int target)  { if (Delta != 0) Add(world, target, Delta, Permanent); }
        public void Revert(EcsWorld world, int source, int target) { if (Delta != 0) Remove(world, target, Delta); }

        /// <summary>Добавить кост-модификатор карте (какой кост-компонент есть: золото/мана/HP). Переиспользуют StealToHand и др.</summary>
        public static void Add(EcsWorld world, int card, int delta, bool permanent)
        {
            // Фидбэк на карте руки, которой изменили стоимость (punch визуала + VFX; no-op если не в руке).
            CardFeedbackUtil.MarkAffectedInHand(world, card, Game.Core.Service.CardAffectKind.CostChanged);
            var g = world.GetPool<GoldCostComponent>();   if (g.Has(card)) { g.Get(card).AddModifier(delta, permanent); NotifyChanged(); return; }
            var m = world.GetPool<ManaCostComponent>();    if (m.Has(card)) { m.Get(card).AddModifier(delta, permanent); NotifyChanged(); return; }
            var h = world.GetPool<HealthCostComponent>();  if (h.Has(card)) { h.Get(card).AddModifier(delta, permanent); NotifyChanged(); }
        }

        /// <summary>ПРИНУДИТЕЛЬНЫЙ кост карты («стоит N, пока условие» — SetCostWhileConditionEffect):
        /// Override в кост-компоненте, база и стек модификаторов игнорируются, снятие возвращает расчёт.</summary>
        public static void SetOverride(EcsWorld world, int card, int value)
        {
            CardFeedbackUtil.MarkAffectedInHand(world, card, Game.Core.Service.CardAffectKind.CostChanged);
            var g = world.GetPool<GoldCostComponent>();   if (g.Has(card)) { g.Get(card).SetOverride(value); NotifyChanged(); return; }
            var m = world.GetPool<ManaCostComponent>();    if (m.Has(card)) { m.Get(card).SetOverride(value); NotifyChanged(); return; }
            var h = world.GetPool<HealthCostComponent>();  if (h.Has(card)) { h.Get(card).SetOverride(value); NotifyChanged(); }
        }

        /// <summary>Снять принудительный кост (парный к SetOverride).</summary>
        public static void ClearOverride(EcsWorld world, int card)
        {
            CardFeedbackUtil.MarkAffectedInHand(world, card, Game.Core.Service.CardAffectKind.CostChanged);
            var g = world.GetPool<GoldCostComponent>();   if (g.Has(card)) { g.Get(card).ClearOverride(); NotifyChanged(); return; }
            var m = world.GetPool<ManaCostComponent>();    if (m.Has(card)) { m.Get(card).ClearOverride(); NotifyChanged(); return; }
            var h = world.GetPool<HealthCostComponent>();  if (h.Has(card)) { h.Get(card).ClearOverride(); NotifyChanged(); }
        }

        static void Remove(EcsWorld world, int card, int delta)
        {
            var g = world.GetPool<GoldCostComponent>();   if (g.Has(card)) { g.Get(card).RemoveModifier(delta); NotifyChanged(); return; }
            var m = world.GetPool<ManaCostComponent>();    if (m.Has(card)) { m.Get(card).RemoveModifier(delta); NotifyChanged(); return; }
            var h = world.GetPool<HealthCostComponent>();  if (h.Has(card)) { h.Get(card).RemoveModifier(delta); NotifyChanged(); }
        }

        // Любое пер-карточное изменение коста → сигнал CardAffordabilitySystem пересчитать руку СРАЗУ
        // (CardCostChangedEvent → окраска/панч на вьюхе). Без этого бафф коста карты УЖЕ В РУКЕ посреди
        // хода ждал бы ближайшего «dirty»-триггера (смена ресурсов/границы хода) — обычно скоро, но не
        // гарантированно в тот же момент. Событие то же, что у глобальной Гиперинфляции — пересчёт дёшев.
        static void NotifyChanged() => Game.Core.Events.GameEventBus.Publish(new Game.Core.Events.CostModifierChangedEvent());
    }

    // === buff: маркер-компонент ===
    public interface IMarker
    {
        void Add(EcsWorld world, int target);
        void Remove(EcsWorld world, int target);
    }

    [Serializable]
    public sealed class ReflectDamageMarker : IMarker
    {
        public void Add(EcsWorld world, int target)    { var p = world.GetPool<ReflectDamageComponent>(); if (!p.Has(target)) p.Add(target); }
        public void Remove(EcsWorld world, int target) { var p = world.GetPool<ReflectDamageComponent>(); if (p.Has(target)) p.Del(target); }
    }

    // Маркер «игрок не получает доход золота в начале хода» (Наступающий кризис). Цель — player-сущность.
    [Serializable]
    public sealed class GoldBlockMarker : IMarker
    {
        public void Add(EcsWorld world, int target)    { var p = world.GetPool<GoldBlockComponent>(); if (!p.Has(target)) p.Add(target); }
        public void Remove(EcsWorld world, int target) { var p = world.GetPool<GoldBlockComponent>(); if (p.Has(target)) p.Del(target); }
    }

    [Serializable]
    public sealed class BuffMarker : IBuffable
    {
        [SerializeReference] public IMarker Marker;
        public void Apply(EcsWorld world, int source, int target)  => Marker?.Add(world, target);
        public void Revert(EcsWorld world, int source, int target) => Marker?.Remove(world, target);
    }

    // === buff: таймер смерти (умрёт через N ходов) ===
    [Serializable]
    public sealed class BuffDeathTimer : IBuffable
    {
        public int Turns = 1;
        public void Apply(EcsWorld world, int source, int target)
        {
            if (Turns <= 0 || target < 0) return;
            var p = world.GetPool<CreatureTimerComponent>();
            if (!p.Has(target)) p.Add(target);
            p.Get(target).TurnsRemaining = Turns;
        }
        public void Revert(EcsWorld world, int source, int target)
        {
            var p = world.GetPool<CreatureTimerComponent>();
            if (p.Has(target)) p.Del(target);
        }
    }

    // === buff: продлить таймер чары (Прокачать чары: разовое «+1 ход» своим чарам на столе). Чара БЕЗ
    // таймера (TurnsAlive=0 — уже постоянная) — no-op, продлевать нечего. Чара с FixedCharmDurationTag
    // (CardCharmModel.FixTurns — «Очарование принцессы» и т.п.) — тоже no-op, длительность зафиксирована.
    // Одноразовый эффект, не аура — Revert пуст, AddBuffEffect{Tracked=false}.
    [Serializable]
    public sealed class ExtendCharmTimer : IBuffable
    {
        public int Turns = 1;
        public void Apply(EcsWorld world, int source, int target)
        {
            if (Turns == 0 || target < 0) return;
            if (world.GetPool<FixedCharmDurationTag>().Has(target)) return;
            var p = world.GetPool<CharmTimerComponent>();
            if (p.Has(target)) p.Get(target).TurnsRemaining += Turns;
        }
        public void Revert(EcsWorld world, int source, int target) { }
    }

    // === class (OOP) === Продлить таймер СВОИХ чар на столе (Прокачать чары: разовое «+1 ход»). NonTarget
    // с прямым обходом EcsFilter, а НЕ AbilityToField{Zone=Board} — тот идёт через TargetGather, а его
    // кандидаты для Zone=Board жёстко ограничены CreatureTag (см. TargetGather.cs): чара туда физически
    // не попадает как кандидат ни при каком фильтре. Переиспользует ExtendCharmTimer поштучно на каждой
    // своей чаре с BoardTag.
    [Serializable]
    public sealed class ExtendOwnCharmsEffect : EffectBase
    {
        public int Turns = 1;

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (Turns == 0 || target < 0) return;   // NonTarget → target = сущность игрока-владельца
            var playerPool = world.GetPool<PlayerComponent>();
            if (!playerPool.Has(target)) return;
            int ownerId = playerPool.Get(target).PlayerId;

            var ownerPool = world.GetPool<OwnerComponent>();
            var buff = new ExtendCharmTimer { Turns = Turns };
            foreach (var e in world.Filter<CharmTag>().Inc<BoardTag>().Inc<OwnerComponent>().End())
            {
                if (ownerPool.Get(e).OwnerId != ownerId) continue;
                buff.Apply(world, cardEntity, e);
            }
        }
    }

    // === buff: урон в конце КАЖДОГО хода (Напустить саранчу) ===
    [Serializable]
    public sealed class BuffRecurringDamage : IBuffable
    {
        public int Amount = 1;
        public void Apply(EcsWorld world, int source, int target)
        {
            if (Amount <= 0 || target < 0) return;
            var p = world.GetPool<RecurringDamageComponent>();
            if (!p.Has(target)) p.Add(target);
            p.Get(target).Amount = Amount;
        }
        public void Revert(EcsWorld world, int source, int target)
        {
            var p = world.GetPool<RecurringDamageComponent>();
            if (p.Has(target)) p.Del(target);
        }
    }

    // === class (OOP) === Навесить бафф на цель. Tracked=false → разово (Грыз перм., модификаторы призыва,
    // маркеры). Tracked=true → аура: идемпотентно (одна цель — раз), трекинг на источнике, авто-откат при
    // смерти источника (RevertOnSourceDeath). СИНК: резолв реплеится по тем же целям → трекинг зеркальный.
    //
    // Duration (ходы владельца ИСТОЧНИКА) + ExpireAt (TurnStart/TurnEnd, тот же момент, что у чар) — АВТО-
    // СПИСЫВАНИЕ конкретно ЭТОЙ выдачи, ортогонально Tracked: можно сочетать с обычной аурой (Tracked=true,
    // «пока жив источник — до 3 ходов максимум») или использовать сам по себе на разовом (Tracked=false)
    // эффекте — «дай цели +2 атаки на 2 хода» без чары-обёртки (BuffDurationTickSystem). Duration=0
    // (умолчание) — без авто-списывания, прежнее поведение. Требует бухгалтерии TrackedBuffsComponent
    // (нужно помнить, ЧТО откатывать) — поэтому Duration>0 включает трекинг-хранение даже при Tracked=false,
    // но БЕЗ идемпотентности ауры (та имеет смысл только для Tracked).
    [Serializable]
    public sealed class AddBuffEffect : EffectBase
    {
        public override Game.Core.Shared.Interface.AiEffectRole AiRole => Game.Core.Shared.Interface.AiEffectRole.BuffAlly;
        [SerializeReference] public IBuffable Buff;
        public bool Tracked = false;
        public bool RevertOnSourceDeath = true;

        [Tooltip("Авто-списывание ЭТОЙ выдачи через N ходов владельца источника. 0 = без длительности (снимается только вручную/по смерти источника, как раньше).")]
        public int Duration = 0;
        [Tooltip("Момент тика длительности: TurnEnd (в конце хода) или TurnStart (в начале хода) — как у чар.")]
        public CharmTickMoment ExpireAt = CharmTickMoment.TurnEnd;

        EcsWorld _world;
        int _source = -1;

        public override void Init(EcsWorld world, int cardEntity, int playerEntity)
        {
            base.Init(world, cardEntity, playerEntity);
            _world = world; _source = cardEntity;
            if (Tracked && RevertOnSourceDeath)
                GameEventBus.Subscribe<CreatureDiedEvent>(this, OnSourceDied);
        }
        public override void Dispose() { GameEventBus.UnsubscribeAll(this); base.Dispose(); }

        void OnSourceDied(CreatureDiedEvent e) { if (e.CardEntity == _source) RevertAll(_world, _source); }

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (Buff == null || target < 0) return;

            if (!Tracked && Duration <= 0) { Buff.Apply(world, cardEntity, target); return; }

            var pool = world.GetPool<TrackedBuffsComponent>();
            if (!pool.Has(cardEntity)) pool.Add(cardEntity).Items = new List<TrackedBuff>();
            ref var t = ref pool.Get(cardEntity);
            t.Items ??= new List<TrackedBuff>();

            if (Tracked)
                for (int i = 0; i < t.Items.Count; i++)
                    if (t.Items[i].Target == target) return;   // идемпотентность ауры: эту цель уже баффали (только Tracked)

            Buff.Apply(world, cardEntity, target);
            t.Items.Add(new TrackedBuff { Target = target, Buff = Buff, TurnsRemaining = Duration, ExpireAt = ExpireAt });
        }

        // Снять все баффы источника (по трекингу). Логика переехала к ДАННЫМ (TrackedBuffs) — её зовёт
        // не только смерть источника, но и полиморф: там источник перестаёт быть собой без гибели.
        internal static void RevertAll(EcsWorld world, int source) => TrackedBuffs.RevertAll(world, source);
    }

    // === class (OOP) === Ручной откат баффов источника (NonTarget) — если у AddBuffEffect выключен
    // RevertOnSourceDeath и снимаешь на своём триггере (напр. конец срока чары).
    [Serializable]
    public sealed class RevertBuffsEffect : EffectBase
    {
        public override void Apply(EcsWorld world, int cardEntity, int target) => AddBuffEffect.RevertAll(world, cardEntity);
    }

    // === class (OOP) === ПАССИВНАЯ АУРА ИЗ ЗОНЫ: «пока эта карта в SourceZone — цели по Filters получают
    // Buff» («Шальной десница»: пока в РУКЕ, другие ваши существа получают +1/+1 к атаке и скорости).
    // НЕ триггер-эффект (паттерн BuffPerCharmEffect): регистрирует PassiveAuraComponent прямо в Init —
    // способность инитится при создании карты на ОБОИХ клиентах, зона проверяется рантайм. Дальше всё
    // держит PassiveAuraSystem: событийный дифф набора целей (новое существо получит бафф само, ушедшее
    // потеряет, источник покинул зону — аура снимется целиком).
    // Кладётся в способность БЕЗ триггеров (напр. AbilityToSelf; Apply — no-op).
    // ЗАЧЕМ отдельно от AddBuffEffect{Tracked}: тот реагирует на СОБЫТИЕ и для «всех существ, включая
    // будущих» требует пары «Field + OnCreatureInvoked», а этот — на СОСТОЯНИЕ, одной способностью.
    [Serializable]
    public sealed class PassiveAuraEffect : EffectBase
    {
        [Tooltip("Где должна лежать карта-ИСТОЧНИК, чтобы аура работала: Hand — «пока в руке» (Шальной " +
                 "десница), Board — обычная аура с поля, Any — всегда.")]
        public TargetZone SourceZone = TargetZone.Hand;

        [Tooltip("Где искать ЦЕЛИ: Board (умолч.) — существа на столе; Hand — существа В РУКЕ (их статы " +
                 "живые и видны на картах); Any — и там, и там.")]
        public TargetZone TargetZone = TargetZone.Board;

        [Tooltip("Что выдаём целям (BuffStat{+1 атака,+1 скорость}, FixStat, BuffMarker…).")]
        [SerializeReference] public IBuffable Buff;

        [Tooltip("Кому: обычно [AllyTargetFilter, NotSelfTargetFilter, CardTypeTargetFilter{Creature}] — " +
                 "«другие ваши существа». Пусто = все кандидаты борда (включая игроков!).")]
        [SerializeReference] public List<ITargetFilter> Filters = new();

        public override void Init(EcsWorld world, int cardEntity, int playerEntity)
        {
            base.Init(world, cardEntity, playerEntity);
            if (Buff == null) return;

            var pool = world.GetPool<PassiveAuraComponent>();
            if (!pool.Has(cardEntity)) pool.Add(cardEntity);
            ref var aura = ref pool.Get(cardEntity);
            aura.SourceZone = SourceZone;
            aura.TargetZone = TargetZone;
            aura.Buff       = Buff;
            aura.Filters    = Filters != null ? Filters.ToArray() : System.Array.Empty<ITargetFilter>();
            aura.Applied  ??= new List<int>();
        }

        public override void Apply(EcsWorld world, int cardEntity, int target) { }   // пассив: считает PassiveAuraSystem
    }

    // === class (OOP) === ПАССИВ «+X/+Y за каждую чару под вашим контролем» (Обжора: +2/+2 за чару).
    // Не триггер-эффект: регистрирует BuffPerCharmComponent на сущности карты прямо в Init (способность
    // инитится при создании карты на ОБОИХ клиентах → зеркально), дальше всё считает BuffPerCharmSystem
    // СОБЫТИЙНО (dirty-флаг по событиям чар/зон, не по-кадрово) диффом стека модификаторов. Бонус действует
    // В РУКЕ И НА СТОЛЕ (решение юзера 2026-07-29). В ассете кладётся в способность БЕЗ триггеров
    // (Triggers пустой — резолвить нечего, Apply no-op) — напр. AbilityToSelf.
    [Serializable]
    public sealed class BuffPerCharmEffect : EffectBase
    {
        public int AttackPerCharm = 2;
        public int HealthPerCharm = 2;

        public override void Init(EcsWorld world, int cardEntity, int playerEntity)
        {
            base.Init(world, cardEntity, playerEntity);
            var pool = world.GetPool<BuffPerCharmComponent>();
            if (!pool.Has(cardEntity)) pool.Add(cardEntity);
            ref var c = ref pool.Get(cardEntity);
            c.AttackPerCharm = AttackPerCharm;
            c.HealthPerCharm = HealthPerCharm;
        }

        public override void Apply(EcsWorld world, int cardEntity, int target) { }   // пассив — считает AuraRecalcSystem
    }
}
