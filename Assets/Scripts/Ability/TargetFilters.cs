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

    /// <summary>Свои ЧАРЫ (на поле, сторона кастера) — как AllyTargetFilter, но для чар вместо существ
    /// (Прокачать чары: «чары под вашим контролем длятся на 1 ход дольше»).</summary>
    [Serializable]
    public sealed class OwnCharmTargetFilter : ITargetSelector
    {
        public bool Match(EcsWorld world, int candidate, int casterCard, int casterPlayer)
        {
            if (!world.GetPool<CharmTag>().Has(candidate)) return false;
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

    /// <summary>Цель — ТОКЕН (IsToken на модели карты, TokenTag на сущности). Для «воздействовать только
    /// на токен-существ» (Командующий королевской гвардией: «ваши токены имеют Двойной удар» и т.п.).</summary>
    [Serializable]
    public sealed class TokenTargetFilter : ITargetFilter
    {
        public bool Match(EcsWorld world, int candidate, int casterCard, int casterPlayer)
            => world.GetPool<TokenTag>().Has(candidate);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // КОМБИНАТОРЫ — алгебра поверх ЛЮБЫХ фильтров, а не «анти-фильтр» под каждый признак.
    // Список Filters уже даёт И; Not даёт НЕ; AnyOf даёт ИЛИ — этого набора хватает на всё.
    // Без них каждое отрицание требовало нового класса: WithoutColorTargetFilter — уже такой
    // рукописный дубль ColorTargetFilter, и следом просились бы «не командир», «не ранен»,
    // «не архетип». Теперь любой существующий и будущий фильтр инвертируется без нового кода.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>ОТРИЦАНИЕ вложенного фильтра: «не командир» = Not{ Inner = CommanderTargetFilter }.
    /// Пустой Inner пропускает всё (нейтральный элемент — недонастроенный фильтр не режет цели молча).</summary>
    [Serializable]
    public sealed class NotTargetFilter : ITargetFilter
    {
        [Tooltip("Фильтр, результат которого инвертируется.")]
        [SerializeReference] public ITargetFilter Inner;

        public bool Match(EcsWorld world, int candidate, int casterCard, int casterPlayer)
            => Inner == null || !Inner.Match(world, candidate, casterCard, casterPlayer);
    }

    /// <summary>ИЛИ по вложенным фильтрам («красное ИЛИ синее»). Сам список Filters у способности
    /// объединяет по И, поэтому ИЛИ выразить было нечем. Пустой список пропускает всё.</summary>
    [Serializable]
    public sealed class AnyOfTargetFilter : ITargetFilter
    {
        [Tooltip("Подходит, если совпал ХОТЯ БЫ один из вложенных фильтров.")]
        [SerializeReference] public System.Collections.Generic.List<ITargetFilter> Any = new();

        public bool Match(EcsWorld world, int candidate, int casterCard, int casterPlayer)
        {
            if (Any == null || Any.Count == 0) return true;
            foreach (var f in Any)
                if (f != null && f.Match(world, candidate, casterCard, casterPlayer)) return true;
            return false;
        }
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

    /// <summary>Оператор сравнения для числовых фильтров (общий: стоимость/статы/что добавим дальше).
    /// Значения ЗАКРЕПЛЕНЫ (как CountSource) — перестановка ломает заавторенные ассеты.</summary>
    public enum CompareOp { Greater = 0, GreaterOrEqual = 1, Less = 2, LessOrEqual = 3, Equal = 4, NotEqual = 5 }

    /// <summary>С ЧЕМ сравнивать числовой признак цели. Значения ЗАКРЕПЛЕНЫ.</summary>
    public enum CompareTo
    {
        FixedValue = 0,   // с числом Value
        SourceCard = 1,   // с тем же признаком КАРТЫ-ИСТОЧНИКА способности («дороже меня»)
    }

    /// <summary>ЧИСЛОВОЙ ПРИЗНАК цели для сравнения. Значения ЗАКРЕПЛЕНЫ; расширяется (добавь ветку в Read).</summary>
    public enum StatKind
    {
        Cost   = 0,   // ТЕКУЩАЯ стоимость (Cost кост-компонента = база + модификаторы карты)
        Attack = 1,
        Health = 2,   // текущее HP
        MaxHealth = 3,
        Speed  = 4,
        Mana   = 5,   // ТЕКУЩИЙ пул маны ИГРОКА (ManaComponent.Current) — читать с сущности-ИГРОКА, не карты
        Gold   = 6,   // ТЕКУЩИЙ пул золота ИГРОКА (GoldComponent.Current) — читать с сущности-ИГРОКА, не карты
    }

    /// <summary>ЧЬЮ сущность подставлять в StatCompareTargetFilter.TryRead / StatSourceUtil.Resolve: сам
    /// источник способности (существо/карта), его ВЛАДЕЛЕЦ-игрок, или ЦЕЛЬ способности (Target — параметр
    /// target у EffectBase.Apply; например TriggerSubject разыгранной владельцем карты — «та же стоимость,
    /// что у только что разыгранного заклинания», Мерлин-пародия). Mana/Gold физически есть только у
    /// игрока — с Self для них TryRead просто не найдёт компонент и вернёт false/0.</summary>
    public enum StatSourceEntity { Self = 0, Owner = 1, Target = 2 }

    /// <summary>Резолвит StatSourceEntity в конкретную сущность для чтения стата (RepeatEffect.CountSource.Stat,
    /// DealDamageEqualToStatEffect и т.п.). Owner читает АКТУАЛЬНОГО владельца через OwnerComponent, а не
    /// кэшированный PlayerEntity — карта могла сменить хозяина (Обращение) уже после Init. Target приходит
    /// СНАРУЖИ (у резолва счёта своей цели нет) — 3-арг. перегрузка сохранена для старых вызовов, Target
    /// без явного targetEntity резолвится в -1 (TryRead просто не найдёт сущность).</summary>
    public static class StatSourceUtil
    {
        public static int Resolve(EcsWorld world, int cardEntity, StatSourceEntity source)
            => Resolve(world, cardEntity, source, -1);

        public static int Resolve(EcsWorld world, int cardEntity, StatSourceEntity source, int targetEntity)
        {
            if (source == StatSourceEntity.Target) return targetEntity;
            if (source == StatSourceEntity.Self) return cardEntity;
            var ownerPool = world.GetPool<OwnerComponent>();
            if (!ownerPool.Has(cardEntity)) return -1;
            return HandSpace.FindPlayerEntity(world, ownerPool.Get(cardEntity).OwnerId);
        }
    }

    /// <summary>
    /// ОБЩИЙ ЧИСЛОВОЙ КОМПАРАТОР цели: «признак цели (Stat) [Op] эталон (число ИЛИ тот же признак
    /// источника)». Один фильтр вместо семейства узких: «дороже меня» (Медлительный дворецкий:
    /// Cost/Greater/SourceCard), «не дороже 3» (Cost/LessOrEqual/Fixed=3), «атака ≥ 5», «раненые»
    /// (Health/Less/SourceCard не годится — для этого DamagedTargetFilter) и т.п.
    /// Стоимости/статы берутся ТЕКУЩИЕ (с модификаторами), а не печатные. Глобальный кост-модификатор
    /// игрока (Гиперинфляция) НЕ учитываем — он у сторон разный, сравнение было бы несимметричным.
    /// Нет нужного компонента у цели (или у источника при SourceCard) → НЕ матчит.
    /// </summary>
    [Serializable]
    public sealed class StatCompareTargetFilter : ITargetFilter
    {
        [Tooltip("Какой признак цели сравниваем.")]
        public StatKind Stat = StatKind.Cost;
        [Tooltip("Оператор: цель [Op] эталон. Greater = «цель больше эталона».")]
        public CompareOp Op = CompareOp.Greater;
        [Tooltip("Эталон: число (FixedValue) или тот же признак КАРТЫ-ИСТОЧНИКА (SourceCard — «дороже меня»).")]
        public CompareTo Compare = CompareTo.SourceCard;
        [Tooltip("Эталон для FixedValue.")]
        public int Value = 0;

        public bool Match(EcsWorld world, int candidate, int casterCard, int casterPlayer)
        {
            if (!TryRead(world, candidate, Stat, out int mine)) return false;

            int reference = Value;
            if (Compare == CompareTo.SourceCard && !TryRead(world, casterCard, Stat, out reference)) return false;

            switch (Op)
            {
                case CompareOp.Greater:        return mine >  reference;
                case CompareOp.GreaterOrEqual: return mine >= reference;
                case CompareOp.Less:           return mine <  reference;
                case CompareOp.LessOrEqual:    return mine <= reference;
                case CompareOp.Equal:          return mine == reference;
                default:                       return mine != reference;
            }
        }

        /// <summary>Текущее значение признака сущности. public — переиспользуют другие фильтры/эффекты.</summary>
        public static bool TryRead(EcsWorld world, int e, StatKind stat, out int value)
        {
            value = 0;
            if (e < 0) return false;
            switch (stat)
            {
                case StatKind.Cost:
                {
                    var g = world.GetPool<GoldCostComponent>();   if (g.Has(e)) { value = g.Get(e).Cost; return true; }
                    var m = world.GetPool<ManaCostComponent>();    if (m.Has(e)) { value = m.Get(e).Cost; return true; }
                    var h = world.GetPool<HealthCostComponent>();  if (h.Has(e)) { value = h.Get(e).Cost; return true; }
                    return false;
                }
                case StatKind.Attack:
                {
                    var p = world.GetPool<AttackComponent>();
                    if (!p.Has(e)) return false;
                    value = p.Get(e).Value; return true;
                }
                case StatKind.Health:
                {
                    var p = world.GetPool<HealthComponent>();
                    if (!p.Has(e)) return false;
                    value = p.Get(e).Current; return true;
                }
                case StatKind.MaxHealth:
                {
                    var p = world.GetPool<HealthComponent>();
                    if (!p.Has(e)) return false;
                    value = p.Get(e).Max; return true;
                }
                case StatKind.Speed:
                {
                    var p = world.GetPool<SpeedComponent>();
                    if (!p.Has(e)) return false;
                    value = p.Get(e).Max; return true;
                }
                case StatKind.Mana:
                {
                    var p = world.GetPool<ManaComponent>();
                    if (!p.Has(e)) return false;
                    value = p.Get(e).Current; return true;
                }
                case StatKind.Gold:
                {
                    var p = world.GetPool<GoldComponent>();
                    if (!p.Has(e)) return false;
                    value = p.Get(e).Current; return true;
                }
                default: return false;
            }
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
