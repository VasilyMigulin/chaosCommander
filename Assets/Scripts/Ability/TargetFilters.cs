using System;
using Game.Core.Ecs.Components;
using Game.Core.Service;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Game.Core.Ability
{
    // === helper ===
    static class TargetFilterUtil
    {
        /// <summary>OwnerId существа (-1 если нет OwnerComponent).</summary>
        public static int OwnerId(EcsWorld world, int entity)
        {
            var pool = world.GetPool<OwnerComponent>();
            return pool.Has(entity) ? pool.Get(entity).OwnerId : -1;
        }

        /// <summary>«Сторона» кастера = PlayerId сущности игрока-кастера (caster-relative, sync-safe).</summary>
        public static int CasterSide(EcsWorld world, int casterPlayer)
        {
            var pool = world.GetPool<PlayerComponent>();
            return pool.Has(casterPlayer) ? pool.Get(casterPlayer).PlayerId : -1;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // СЕЛЕКТОРЫ (ITargetSelector) — категории целей, объединяются по ИЛИ. Всё
    // caster-relative (относительно владельца способности), без статичных тегов —
    // иначе реактивные способности на чужом ходу били бы не туда (синк, см. ниже).
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Вражеские СУЩЕСТВА (на поле, чужая сторона). Для враж. игрока — OpponentTargetFilter.</summary>
    [Serializable]
    public sealed class EnemyTargetFilter : ITargetSelector
    {
        public bool Match(EcsWorld world, int candidate, int casterCard, int casterPlayer)
        {
            if (!world.GetPool<CreatureTag>().Has(candidate)) return false;
            int side = TargetFilterUtil.OwnerId(world, candidate);
            return side != -1 && side != TargetFilterUtil.CasterSide(world, casterPlayer);
        }
    }

    /// <summary>Свои СУЩЕСТВА (на поле, сторона кастера). Для своего игрока — OwnPlayerTargetFilter.</summary>
    [Serializable]
    public sealed class AllyTargetFilter : ITargetSelector
    {
        public bool Match(EcsWorld world, int candidate, int casterCard, int casterPlayer)
        {
            if (!world.GetPool<CreatureTag>().Has(candidate)) return false;
            int side = TargetFilterUtil.OwnerId(world, candidate);
            return side != -1 && side == TargetFilterUtil.CasterSide(world, casterPlayer);
        }
    }

    /// <summary>Вражеский ИГРОК (сущность игрока на чужой стороне).</summary>
    [Serializable]
    public sealed class OpponentTargetFilter : ITargetSelector
    {
        public bool Match(EcsWorld world, int candidate, int casterCard, int casterPlayer)
            => world.GetPool<PlayerComponent>().Has(candidate) && candidate != casterPlayer;
    }

    /// <summary>Свой ИГРОК (сущность игрока-кастера).</summary>
    [Serializable]
    public sealed class OwnPlayerTargetFilter : ITargetSelector
    {
        public bool Match(EcsWorld world, int candidate, int casterCard, int casterPlayer)
            => world.GetPool<PlayerComponent>().Has(candidate) && candidate == casterPlayer;
    }

    /// <summary>Карта принадлежит ОППОНЕНТУ (по OwnerComponent), БЕЗ требования быть существом — для карт в
    /// ЗОНАХ (рука/колода/кладбище): дискард/похищение/etc. На борде существа фильтруй Enemy/Ally.</summary>
    [Serializable]
    public sealed class OpponentOwnedTargetFilter : ITargetSelector
    {
        public bool Match(EcsWorld world, int candidate, int casterCard, int casterPlayer)
        {
            int side = TargetFilterUtil.OwnerId(world, candidate);
            return side != -1 && side != TargetFilterUtil.CasterSide(world, casterPlayer);
        }
    }

    /// <summary>ТВОЯ карта (принадлежит стороне кастера по OwnerComponent), БЕЗ требования быть существом —
    /// для карт в ЗОНАХ (своя колода/рука/кладбище): Барабук/Библиотекарь/Патриарх. Зеркало
    /// OpponentOwnedTargetFilter. На борде «свои существа» — AllyTargetFilter.</summary>
    [Serializable]
    [MovedFrom(true, sourceClassName: "OwnerOwnedTargetFilter")]
    public sealed class CasterOwnedTargetFilter : ITargetSelector
    {
        public bool Match(EcsWorld world, int candidate, int casterCard, int casterPlayer)
        {
            int side = TargetFilterUtil.OwnerId(world, candidate);
            return side != -1 && side == TargetFilterUtil.CasterSide(world, casterPlayer);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ОГРАНИЧИТЕЛИ (обычный ITargetFilter) — сужают по И поверх селекторов.
    // Только ОРТОГОНАЛЬНЫЕ селекторам признаки (цвет/ранен/командир/не-сам).
    // «Тип/сторону» (существо/игрок, свой/чужой) задают селекторы, не ограничители.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Цель содержит указанный цвет (для существ).</summary>
    [Serializable]
    public sealed class ColorTargetFilter : ITargetFilter
    {
        public EnumService.Element Color;

        public bool Match(EcsWorld world, int candidate, int casterCard, int casterPlayer)
        {
            var pool = world.GetPool<CardModelComponent>();
            return pool.Has(candidate) && (pool.Get(candidate).Element & Color) != 0;
        }
    }

    /// <summary>Любая цель, КРОМЕ самого источника способности.</summary>
    [Serializable]
    public sealed class NotSelfTargetFilter : ITargetFilter
    {
        public bool Match(EcsWorld world, int candidate, int casterCard, int casterPlayer)
            => candidate != casterCard;
    }

    /// <summary>Раненая цель (Current &lt; Max) — напр. для лечения.</summary>
    [Serializable]
    public sealed class DamagedTargetFilter : ITargetFilter
    {
        public bool Match(EcsWorld world, int candidate, int casterCard, int casterPlayer)
        {
            var pool = world.GetPool<HealthComponent>();
            return pool.Has(candidate) && pool.Get(candidate).Current < pool.Get(candidate).Max;
        }
    }

    /// <summary>Цель — командир.</summary>
    [Serializable]
    public sealed class CommanderTargetFilter : ITargetFilter
    {
        public bool Match(EcsWorld world, int candidate, int casterCard, int casterPlayer)
            => world.GetPool<CommanderTag>().Has(candidate);
    }

    /// <summary>Цель БЕЗ указанного цвета (Окропить: «существам без жёлтого»). Игроки (без
    /// CardModelComponent) не подходят → бьёт только существ без нужного цвета.</summary>
    [Serializable]
    public sealed class WithoutColorTargetFilter : ITargetFilter
    {
        public EnumService.Element Color;

        public bool Match(EcsWorld world, int candidate, int casterCard, int casterPlayer)
        {
            var pool = world.GetPool<CardModelComponent>();
            return pool.Has(candidate) && (pool.Get(candidate).Element & Color) == 0;
        }
    }

    /// <summary>Цель — карта заданного ТИПА (Spell/Creature/Charm). Ограничитель: для выборки из зон
    /// (напр. «случайное ЗАКЛИНАНИЕ из колоды» — Библиотекарь).</summary>
    [Serializable]
    public sealed class CardTypeTargetFilter : ITargetFilter
    {
        public EnumService.CardType CardType;

        public bool Match(EcsWorld world, int candidate, int casterCard, int casterPlayer)
        {
            var pool = world.GetPool<CardModelComponent>();
            return pool.Has(candidate) && pool.Get(candidate).CardType == CardType;
        }
    }

    /// <summary>Цель стоит НЕ ДОРОЖЕ MaxCost (Барабук: «спелл за 3 и меньше»). Ограничитель: смотрит
    /// фактический кост-компонент карты (Gold/Mana/Health), какой есть. Без кост-компонента → не подходит.</summary>
    [Serializable]
    public sealed class CostAtMostTargetFilter : ITargetFilter
    {
        public int MaxCost = 3;

        public bool Match(EcsWorld world, int candidate, int casterCard, int casterPlayer)
        {
            var g = world.GetPool<GoldCostComponent>();   if (g.Has(candidate)) return g.Get(candidate).Cost <= MaxCost;
            var m = world.GetPool<ManaCostComponent>();    if (m.Has(candidate)) return m.Get(candidate).Cost <= MaxCost;
            var h = world.GetPool<HealthCostComponent>();  if (h.Has(candidate)) return h.Get(candidate).Cost <= MaxCost;
            return false;
        }
    }

    /// <summary>Цель принадлежит АРХЕТИПУ (работяга/чёрт/...). Ограничитель: комбинируй с селектором
    /// (Ally/Enemy → «свои работяги»). Archetype — тот же [SerializeReference] ICreatureTag, что и на
    /// карте; матчинг идёт через его Has (без switch/маппинга). Null → не матчит.</summary>
    [Serializable]
    public sealed class ArchetypeTargetFilter : ITargetFilter
    {
        [SerializeReference] public ICreatureTag Archetype;

        public bool Match(EcsWorld world, int candidate, int casterCard, int casterPlayer)
            => Archetype != null && Archetype.Has(world, candidate);
    }

    /// <summary>Цель ЕЩЁ НЕ получала tracked-бафф от ЭТОГО источника. ОБЩЕЕ ПРАВИЛО для ЛЮБОЙ реактивной
    /// ауры (OnCreatureInvokedTrigger + tracked-бафф, напр. Селекционер/Начальник смены): без этого
    /// фильтра эффект (AddBuffEffect{Tracked} ИЛИ легаси ApplyTrackedBuffEffect) сам молча скипает уже
    /// забаффанную цель (идемпотентность на уровне эффекта), но таргетинг всё равно возвращает её в
    /// Targets — способность выглядит «сработавшей» (играет каст-анимация) при КАЖДОМ чужом триггере,
    /// хотя реально бафать уже некого. С этим фильтром в списке Filters таргетинг сам отсеивает уже
    /// забаффанные цели → Targets пуст, когда бафать некого, и анимация не запускается (см.
    /// RunResolveAbilityQueueSystem.hasTargets).
    /// Проверяет ОБА варианта трекинга — новый (TrackedBuffsComponent, AddBuffEffect{Tracked}) и легаси
    /// (AppliedBuffsComponent, ApplyTrackedBuffEffect) — так один и тот же фильтр годится для карт на
    /// любом из двух эффектов, без миграции старых ассетов.</summary>
    [Serializable]
    public sealed class NotAlreadyTrackedTargetFilter : ITargetFilter
    {
        public bool Match(EcsWorld world, int candidate, int casterCard, int casterPlayer)
        {
            var trackedPool = world.GetPool<TrackedBuffsComponent>();
            if (trackedPool.Has(casterCard))
            {
                var items = trackedPool.Get(casterCard).Items;
                if (items != null)
                    for (int i = 0; i < items.Count; i++)
                        if (items[i].Target == candidate) return false;
            }

            var appliedPool = world.GetPool<AppliedBuffsComponent>();
            if (appliedPool.Has(casterCard))
            {
                var records = appliedPool.Get(casterCard).Records;
                if (records != null)
                    for (int i = 0; i < records.Count; i++)
                        if (records[i].Target == candidate) return false;
            }

            return true;
        }
    }
}
