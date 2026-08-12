using System;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Leopotam.EcsLite;

namespace Game.Core.Ability
{
    // ─────────────────────────────────────────────────────────────────────────
    // РЕАКТИВНЫЕ АУРЫ (решение пользователя): НЕ continuous-recompute. Источник реактивно выдаёт
    // баффы целям и ТРЕКАЕТ их на себе (AppliedBuffsComponent); при снятии (источник умер/ушёл) —
    // откатывает. Никакого покадрового пересчёта.
    //
    // Композиция (через триггеры+таргетинг, без спец-логики):
    //   «пока контролируете → ваши X +A/+H»: OnCast(self)+OnCreatureInvoked → AbilityToField{filter} +
    //       ApplyTrackedBuffEffect;  OnDie(self) → AbilityNonTarget + RevertTrackedBuffsEffect.
    //   «N ходов»: то же + DeathTimerEffect{N} на источнике → его OnDie дёрнет реверт.
    //
    // ApplyTracked применяется ПО ОДНОЙ цели (цели собирает AbilityToField), идемпотентно: уже
    // забаффанную пропускает (трекинг на источнике). Бафф — МЯГКИЙ модификатор (AddModifier): если цель
    // умрёт, DieSystem сам очистит её мягкие модификаторы (реверт по трекингу тогда просто no-op).
    //
    // СИНК: триггеры — на активе; резолв ауры реплеится по тем же целям (ActionAbilityData.TargetKeys),
    // пассив применяет ApplyTracked к тем же → трекинг строится зеркально на обоих, спец-канал не нужен.
    // КАВЕАТЫ: реверт привязан к OnDie источника (bounce в руку/колоду реверт не дёрнет); «динамику»
    // (цель, получившая подходящий архетип ПОЗЖЕ) не ловит — для набора достаточно.
    // ─────────────────────────────────────────────────────────────────────────

    // === class (OOP) === Выдать цели бафф и ЗАТРЕКАТЬ на источнике (идемпотентно). target — существо
    // (собран AbilityToField по фильтру); cardEntity — источник ауры. Сам держит жизненный цикл:
    // при RevertOnSourceDeath (по умолч.) подписывается на смерть источника и САМ откатывает баффы —
    // тогда аура авторится ОДНОЙ способностью (отдельная revert-способность не нужна). Синк: смерть
    // синкается на обоих клиентах → CreatureDiedEvent у обоих → ревёрт зеркальный, ability-канал не нужен.
    [Serializable]
    public sealed class ApplyTrackedBuffEffect : EffectBase
    {
        public int AttackBonus = 0;
        public int HealthBonus = 0;
        public int SpeedBonus  = 0;

        // Снять баффы, когда источник погибнет (аура в одну способность). Выключи, если ревёрт
        // вешаешь вручную на другой триггер (через RevertTrackedBuffsEffect).
        public bool RevertOnSourceDeath = true;

        // false (аура): на одну цель — один бафф (дедуп — чтобы переприменение по OnCreatureInvoked не
        // стакало). true: стакать (можно бафать одну цель несколько раз, напр. RepeatEffect на себя).
        public bool Stack = false;

        EcsWorld _world;
        int _source = -1;

        public override void Init(EcsWorld world, int cardEntity, int playerEntity)
        {
            base.Init(world, cardEntity, playerEntity);
            _world = world;
            _source = cardEntity;
            if (RevertOnSourceDeath)
                GameEventBus.Subscribe<CreatureDiedEvent>(this, OnSourceDied);
        }

        public override void Dispose()
        {
            GameEventBus.UnsubscribeAll(this);
            base.Dispose();
        }

        void OnSourceDied(CreatureDiedEvent e)
        {
            if (e.CardEntity != _source) return;          // погиб именно наш источник?
            RevertTrackedBuffs(_world, _source);
        }

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (target < 0) return;

            var buffsPool = world.GetPool<AppliedBuffsComponent>();

            if (!buffsPool.Has(cardEntity))
                buffsPool.Add(cardEntity).Records = new System.Collections.Generic.List<BuffRecord>();

            ref var buffComp = ref buffsPool.Get(cardEntity);
            buffComp.Records ??= new System.Collections.Generic.List<BuffRecord>();

            // идемпотентность (только для ауры, Stack=false): эту цель этой аурой уже баффали?
            if (!Stack)
                for (int i = 0; i < buffComp.Records.Count; i++)
                    if (buffComp.Records[i].Target == target) return;

            bool applied = false;
            if (AttackBonus != 0)
            {
                var p = world.GetPool<AttackComponent>();
                if (p.Has(target)) { ref var a = ref p.Get(target); a.AddModifier(AttackBonus); applied = true; }
            }
            if (HealthBonus != 0)
            {
                var p = world.GetPool<HealthComponent>();
                if (p.Has(target)) { ref var h = ref p.Get(target); h.AddModifier(HealthBonus); if (HealthBonus > 0) h.Current += HealthBonus; applied = true; }
            }
            if (SpeedBonus != 0)
            {
                var p = world.GetPool<SpeedComponent>();
                if (p.Has(target)) { ref var s = ref p.Get(target); s.AddModifier(SpeedBonus); applied = true; }
            }

            if (applied)
                buffComp.Records.Add(new BuffRecord { Target = target, Atk = AttackBonus, Hp = HealthBonus, Speed = SpeedBonus });
        }

        // Снять ВСЕ баффы, выданные аурой источника (по трекингу), и очистить трекинг. Мёртвые цели
        // уже очищены DieSystem → RemoveModifier по ним просто no-op. Общий для авто- и ручного ревёрта.
        // Логика в Ecs.Components.AppliedBuffs (видна и Ecs.Systems — RunTransformSystem откатывает эту же
        // ауру при полиморфе источника, а Ecs.Systems не видит эту сборку) — тут просто делегируем.
        internal static void RevertTrackedBuffs(EcsWorld world, int source)
            => Game.Core.Ecs.Components.AppliedBuffs.RevertAll(world, source);
    }

    // === class (OOP) === РУЧНОЙ ревёрт ауры (NonTarget). Нужен ТОЛЬКО если у ApplyTrackedBuffEffect
    // выключен RevertOnSourceDeath и ты хочешь снять баффы на своём триггере (напр. конец хода).
    // Для обычной ауры «пока контролируете» НЕ нужен — ApplyTracked сам снимает при смерти источника.
    [Serializable]
    public sealed class RevertTrackedBuffsEffect : EffectBase
    {
        public override void Apply(EcsWorld world, int cardEntity, int target)
            => ApplyTrackedBuffEffect.RevertTrackedBuffs(world, cardEntity);
    }
}
