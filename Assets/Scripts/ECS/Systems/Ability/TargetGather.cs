using System.Collections.Generic;
using Game.Core.Ecs.Components;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;

namespace Game.Core.Ecs.Systems
{
    // === helper === сбор кандидатов-целей с доски (существа) и игроков, с учётом области и фильтров.
    static class TargetGather
    {
        /// <param name="area">null → без ограничения по стороне (Target/Random, вся зона).</param>
        /// <param name="zone">Board (существа на поле + игроки) | Deck/Hand/Grave (карты в зоне, без игроков).</param>
        public static List<int> Gather(EcsWorld world, ITargetFilter[] filters,
                                       int casterCard, int casterPlayer, FieldArea? area,
                                       TargetZone zone = TargetZone.Board)
        {
            var result = new List<int>();
            int casterOwnerId = OwnerId(world, casterCard);
            var ownerPool = world.GetPool<OwnerComponent>();

            if (zone == TargetZone.Board)
            {
                foreach (var e in world.Filter<CreatureTag>().Inc<BoardTag>().Exc<DeadTag>().End())
                {
                    int side = ownerPool.Has(e) ? ownerPool.Get(e).OwnerId : -1;
                    if (!AreaOk(area, side, casterOwnerId)) continue;
                    if (!FiltersOk(world, e, casterCard, casterPlayer, filters)) continue;
                    result.Add(e);
                }

                var playerPool = world.GetPool<PlayerComponent>();
                foreach (var e in world.Filter<PlayerComponent>().End())
                {
                    int side = playerPool.Get(e).PlayerId;
                    if (!AreaOk(area, side, casterOwnerId)) continue;
                    if (!FiltersOk(world, e, casterCard, casterPlayer, filters)) continue;
                    result.Add(e);
                }
                return result;
            }

            // Не-Board зоны: карты с соответствующим тегом (без игроков). «Сторона» = OwnerComponent.
            var commanderPool = world.GetPool<CommanderTag>();
            foreach (var e in ZoneCards(world, zone))
            {
                if (e == casterCard) continue;        // карта-источник не таргетит сама себя в зоне (иначе Random тратит пик впустую)
                if (commanderPool.Has(e)) continue;   // командир неуязвим к дискарду/миллу/похищению из зоны
                int side = ownerPool.Has(e) ? ownerPool.Get(e).OwnerId : -1;
                if (!AreaOk(area, side, casterOwnerId)) continue;
                if (!FiltersOk(world, e, casterCard, casterPlayer, filters)) continue;
                result.Add(e);
            }
            return result;
        }

        static EcsFilter ZoneCards(EcsWorld world, TargetZone zone)
        {
            switch (zone)
            {
                case TargetZone.Deck:  return world.Filter<DeckTag>().Inc<OwnerComponent>().End();
                case TargetZone.Hand:  return world.Filter<HandTag>().Inc<OwnerComponent>().End();
                case TargetZone.Grave: return world.Filter<GraveTag>().Inc<OwnerComponent>().End();
                default:               return world.Filter<DeckTag>().Inc<OwnerComponent>().End();
            }
        }

        public static bool FiltersOk(EcsWorld world, int candidate, int casterCard, int casterPlayer, ITargetFilter[] filters)
        {
            if (filters == null) return true;

            // Селекторы (ITargetSelector) = категории целей, объединяются по ИЛИ.
            // Ограничители (прочие ITargetFilter) = сужают по И. Нет селекторов → стадия пройдена.
            bool hasSelector = false;
            bool selectorHit = false;
            foreach (var f in filters)
            {
                if (f == null) continue;
                if (f is ITargetSelector)
                {
                    hasSelector = true;
                    if (f.Match(world, candidate, casterCard, casterPlayer)) selectorHit = true;
                }
                else if (!f.Match(world, candidate, casterCard, casterPlayer))
                {
                    return false;   // ограничитель не прошёл
                }
            }
            return !hasSelector || selectorHit;
        }

        static bool AreaOk(FieldArea? area, int side, int casterOwnerId)
        {
            if (area == null) return true;
            switch (area.Value)
            {
                case FieldArea.Own:   return side == casterOwnerId;
                case FieldArea.Enemy: return side != casterOwnerId;
                default:              return true; // All
            }
        }

        static int OwnerId(EcsWorld world, int entity)
        {
            var pool = world.GetPool<OwnerComponent>();
            return pool.Has(entity) ? pool.Get(entity).OwnerId : -1;
        }
    }
}
