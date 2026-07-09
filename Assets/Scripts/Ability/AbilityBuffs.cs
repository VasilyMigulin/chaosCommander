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
            if (Health != 0) { var p = world.GetPool<HealthComponent>(); if (p.Has(target)) { ref var h = ref p.Get(target); h.AddModifier(Health, Permanent); if (Health > 0) h.Current += Health; } }
            if (Speed  != 0) { var p = world.GetPool<SpeedComponent>();  if (p.Has(target)) p.Get(target).AddModifier(Speed, Permanent); }
        }
        public void Revert(EcsWorld world, int source, int target)
        {
            if (Attack != 0) { var p = world.GetPool<AttackComponent>(); if (p.Has(target)) p.Get(target).RemoveModifier(Attack); }
            if (Health != 0) { var p = world.GetPool<HealthComponent>(); if (p.Has(target)) p.Get(target).RemoveModifier(Health); }
            if (Speed  != 0) { var p = world.GetPool<SpeedComponent>();  if (p.Has(target)) p.Get(target).RemoveModifier(Speed); }
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
            var g = world.GetPool<GoldCostComponent>();   if (g.Has(card)) { g.Get(card).AddModifier(delta, permanent); return; }
            var m = world.GetPool<ManaCostComponent>();    if (m.Has(card)) { m.Get(card).AddModifier(delta, permanent); return; }
            var h = world.GetPool<HealthCostComponent>();  if (h.Has(card)) { h.Get(card).AddModifier(delta, permanent); }
        }

        static void Remove(EcsWorld world, int card, int delta)
        {
            var g = world.GetPool<GoldCostComponent>();   if (g.Has(card)) { g.Get(card).RemoveModifier(delta); return; }
            var m = world.GetPool<ManaCostComponent>();    if (m.Has(card)) { m.Get(card).RemoveModifier(delta); return; }
            var h = world.GetPool<HealthCostComponent>();  if (h.Has(card)) { h.Get(card).RemoveModifier(delta); }
        }
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

    // === class (OOP) === Навесить бафф на цель. Tracked=false → разово (Грыз перм., модификаторы призыва,
    // маркеры). Tracked=true → аура: идемпотентно (одна цель — раз), трекинг на источнике, авто-откат при
    // смерти источника (RevertOnSourceDeath). СИНК: резолв реплеится по тем же целям → трекинг зеркальный.
    [Serializable]
    public sealed class AddBuffEffect : EffectBase
    {
        public override Game.Core.Shared.Interface.AiEffectRole AiRole => Game.Core.Shared.Interface.AiEffectRole.BuffAlly;
        [SerializeReference] public IBuffable Buff;
        public bool Tracked = false;
        public bool RevertOnSourceDeath = true;

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

            if (!Tracked) { Buff.Apply(world, cardEntity, target); return; }

            var pool = world.GetPool<TrackedBuffsComponent>();
            if (!pool.Has(cardEntity)) pool.Add(cardEntity).Items = new List<TrackedBuff>();
            ref var t = ref pool.Get(cardEntity);
            t.Items ??= new List<TrackedBuff>();
            for (int i = 0; i < t.Items.Count; i++)
                if (t.Items[i].Target == target) return;   // идемпотентность ауры: эту цель уже баффали

            Buff.Apply(world, cardEntity, target);
            t.Items.Add(new TrackedBuff { Target = target, Buff = Buff });
        }

        // Снять все баффы источника (по трекингу). Мёртвые цели уже очищены DieSystem → Revert по ним no-op.
        internal static void RevertAll(EcsWorld world, int source)
        {
            var pool = world.GetPool<TrackedBuffsComponent>();
            if (!pool.Has(source)) return;
            ref var t = ref pool.Get(source);
            if (t.Items == null) return;
            foreach (var it in t.Items) it.Buff?.Revert(world, source, it.Target);
            t.Items.Clear();
        }
    }

    // === class (OOP) === Ручной откат баффов источника (NonTarget) — если у AddBuffEffect выключен
    // RevertOnSourceDeath и снимаешь на своём триггере (напр. конец срока чары).
    [Serializable]
    public sealed class RevertBuffsEffect : EffectBase
    {
        public override void Apply(EcsWorld world, int cardEntity, int target) => AddBuffEffect.RevertAll(world, cardEntity);
    }
}
