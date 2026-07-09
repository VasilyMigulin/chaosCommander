using System;
using Game.Core.Ecs.Components;
using Game.Core.Service;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;
using UnityEngine;

namespace Game.Core.Ability
{
    // ─────────────────────────────────────────────────────────────────────────
    // БИБЛИОТЕКА ПРАВИЛ — «можно ли активировать способность СЕЙЧАС». Pull-стиль
    // (Evaluate в момент срабатывания, без подписок) → проще и надёжнее условий.
    // Прогоняет RunCheckAbilityRulesSystem (группы Rule: AND между группами,
    // AllOf/AnyOf внутри). Зону проверяем ЗДЕСЬ, а не в подписке триггера.
    // ─────────────────────────────────────────────────────────────────────────

    // === helper ===
    internal static class RuleUtil
    {
        public static int OwnerId(EcsWorld world, int entity)
        {
            var pool = world.GetPool<OwnerComponent>();
            return pool.Has(entity) ? pool.Get(entity).OwnerId : -1;
        }

        /// <summary>Сколько живых существ на поле у владельца ownerId (excludeSelf — без указанной карты).</summary>
        public static int CountCreaturesOnBoard(EcsWorld world, int ownerId, int excludeSelf = -1)
        {
            var owner = world.GetPool<OwnerComponent>();
            int n = 0;
            foreach (var e in world.Filter<CreatureTag>().Inc<BoardTag>().Inc<OwnerComponent>().Exc<DeadTag>().End())
            {
                if (e == excludeSelf) continue;
                if (owner.Get(e).OwnerId == ownerId) n++;
            }
            return n;
        }

        /// <summary>Есть ли в КОЛОДЕ владельца ownerId хоть одно существо (архетип «без существ»: экзотик
        /// «Я сам всё сделаю!», Пустой фолиант). Скан по тегам (не по спискам DeckComponent — списки
        /// оппонента у пассива могут быть неточны, теги зеркальны).</summary>
        public static bool DeckHasCreatures(EcsWorld world, int ownerId)
        {
            var owner = world.GetPool<OwnerComponent>();
            foreach (var e in world.Filter<CreatureTag>().Inc<DeckTag>().Inc<OwnerComponent>().End())
                if (owner.Get(e).OwnerId == ownerId) return true;
            return false;
        }
    }

    // === class (OOP) === Источник на поле (борд).
    [Serializable]
    public sealed class SourceOnBoardRule : IRule
    {
        public bool Evaluate(EcsWorld world, int cardEntity, int playerEntity)
            => world.GetPool<BoardTag>().Has(cardEntity);
    }

    // === class (OOP) === Источник в руке.
    [Serializable]
    public sealed class SourceInHandRule : IRule
    {
        public bool Evaluate(EcsWorld world, int cardEntity, int playerEntity)
            => world.GetPool<HandTag>().Has(cardEntity);
    }

    // === class (OOP) === Игрок контролирует не меньше MinCount существ на поле
    // (по умолчанию не считая сам источник).
    [Serializable]
    public sealed class ControlsMinCreaturesRule : IRule
    {
        public int  MinCount    = 1;
        public bool ExcludeSelf = true;

        public bool Evaluate(EcsWorld world, int cardEntity, int playerEntity)
        {
            int ownerId = RuleUtil.OwnerId(world, cardEntity);
            if (ownerId < 0) return false;
            return RuleUtil.CountCreaturesOnBoard(world, ownerId, ExcludeSelf ? cardEntity : -1) >= MinCount;
        }
    }

    // === class (OOP) === Оппонент контролирует не меньше MinCount существ на поле.
    [Serializable]
    public sealed class EnemyControlsCreatureRule : IRule
    {
        public int MinCount = 1;

        public bool Evaluate(EcsWorld world, int cardEntity, int playerEntity)
        {
            int mine = RuleUtil.OwnerId(world, cardEntity);
            if (mine < 0) return false;

            var owner = world.GetPool<OwnerComponent>();
            int n = 0;
            foreach (var e in world.Filter<CreatureTag>().Inc<BoardTag>().Inc<OwnerComponent>().Exc<DeadTag>().End())
            {
                int o = owner.Get(e).OwnerId;
                if (o != -1 && o != mine) n++;
            }
            return n >= MinCount;
        }
    }

    // === class (OOP) === На поле есть >= MinCount живых существ цвета Color на указанной стороне (Side).
    // ГЕЙТ розыгрыша (а не фильтр!): Отлучение «лишить жёлтого» кастуется ТОЛЬКО если жёлтое существо есть —
    // иначе фильтр-таргет ушёл бы в режим выбора без целей → софт-лок. Side: All=любая, Own=своя, Enemy=чужая.
    [Serializable]
    public sealed class CreatureOfColorOnBoardRule : IRule
    {
        public EnumService.Element Color;
        public FieldArea Side = FieldArea.All;
        public int MinCount = 1;

        public bool Evaluate(EcsWorld world, int cardEntity, int playerEntity)
        {
            int mine = RuleUtil.OwnerId(world, cardEntity);
            var owner = world.GetPool<OwnerComponent>();
            var model = world.GetPool<CardModelComponent>();
            int n = 0;
            foreach (var e in world.Filter<CreatureTag>().Inc<BoardTag>().Inc<OwnerComponent>().Inc<CardModelComponent>().Exc<DeadTag>().End())
            {
                if ((model.Get(e).Element & Color) == 0) continue;          // нужного цвета?
                int o = owner.Get(e).OwnerId;
                if (Side == FieldArea.Own   && o != mine) continue;
                if (Side == FieldArea.Enemy && (o == mine || o < 0)) continue;
                n++;
            }
            return n >= MinCount;
        }
    }

    // === class (OOP) === Здоровье игрока-владельца ниже порога.
    [Serializable]
    public sealed class OwnerHealthBelowRule : IRule
    {
        public int Threshold = 10;

        public bool Evaluate(EcsWorld world, int cardEntity, int playerEntity)
        {
            var hp = world.GetPool<HealthComponent>();
            return hp.Has(playerEntity) && hp.Get(playerEntity).Current < Threshold;
        }
    }

    // === class (OOP) === Здоровье существа-источника ниже порога.
    [Serializable]
    public sealed class SourceHealthBelowRule : IRule
    {
        public int Threshold = 1;

        public bool Evaluate(EcsWorld world, int cardEntity, int playerEntity)
        {
            var hp = world.GetPool<HealthComponent>();
            return hp.Has(cardEntity) && hp.Get(cardEntity).Current < Threshold;
        }
    }

    // === class (OOP) === Игрок контролирует на поле конкретную карту-партнёра (Биба/Боба:
    // «если контролируете Бобу»). Партнёр задаётся ассетом (ICreatable), матч по ExpansionId+CardId.
    [Serializable]
    public sealed class ControlsNamedCreatureRule : IRule
    {
        [Tooltip("Ассет CardInstanceData партнёра (перетащить).")]
        public ScriptableObject Source;

        public bool Evaluate(EcsWorld world, int cardEntity, int playerEntity)
        {
            var partner = Source as ICreatable;
            if (partner == null) return false;

            int ownerId = RuleUtil.OwnerId(world, cardEntity);
            if (ownerId < 0) return false;

            var owner = world.GetPool<OwnerComponent>();
            var model = world.GetPool<CardModelComponent>();
            foreach (var e in world.Filter<CreatureTag>().Inc<BoardTag>().Inc<OwnerComponent>().Inc<CardModelComponent>().Exc<DeadTag>().End())
            {
                if (owner.Get(e).OwnerId != ownerId) continue;
                ref var m = ref model.Get(e);
                if (m.ModelId == partner.CardId && m.ExpansionId == partner.ExpansionId) return true;
            }
            return false;
        }
    }

    // === class (OOP) === Кол-во карт АРХЕТИПА в зоне (колода/рука/борд) в диапазоне [MinCount..MaxCount].
    // Универсальное правило-проверка содержимого зоны. Примеры:
    //   • «нет работяг в колоде» (Латентный работяга при смерти): Zone=Deck, Archetype=Worker, MaxCount=0;
    //   • «есть хотя бы 1 чёрт в руке»: Zone=Hand, Archetype=Imp, MinCount=1.
    // Сам источник не считается. OwnerOwned=true → карты владельца источника; false → оппонента.
    // (Цвет/тип проверяются другими правилами на борде — CreatureOfColorOnBoardRule и т.п.; здесь — архетип
    // в любой зоне, т.к. готового деко/рука-по-архетипу не было.)
    [Serializable]
    public sealed class ZoneArchetypeCountRule : IRule
    {
        public enum ZoneKind { Deck, Hand, Board }
        public ZoneKind Zone = ZoneKind.Deck;
        [SerializeReference] public ICreatureTag Archetype;
        public int MinCount = 0;                // нижняя граница (включительно)
        public int MaxCount = int.MaxValue;     // верхняя граница (включительно); «нет в зоне» → MaxCount=0
        public bool OwnerOwned = true;          // true = карты владельца источника; false = оппонента

        public bool Evaluate(EcsWorld world, int cardEntity, int playerEntity)
        {
            if (Archetype == null) return false;
            int ownerId = RuleUtil.OwnerId(world, cardEntity);
            if (ownerId < 0) return false;

            var ownerPool = world.GetPool<OwnerComponent>();
            int n = 0;
            foreach (var e in ZoneFilter(world))
            {
                if (e == cardEntity) continue;                       // не считаем сам источник
                if (!ownerPool.Has(e)) continue;
                bool isOwner = ownerPool.Get(e).OwnerId == ownerId;
                if (OwnerOwned != isOwner) continue;
                if (!Archetype.Has(world, e)) continue;
                n++;
            }
            return n >= MinCount && n <= MaxCount;
        }

        EcsFilter ZoneFilter(EcsWorld world)
        {
            switch (Zone)
            {
                case ZoneKind.Hand:  return world.Filter<HandTag>().Inc<OwnerComponent>().End();
                case ZoneKind.Board: return world.Filter<BoardTag>().Inc<OwnerComponent>().Exc<DeadTag>().End();
                default:             return world.Filter<DeckTag>().Inc<OwnerComponent>().End();
            }
        }
    }
}
