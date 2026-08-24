using System.Collections.Generic;
using Leopotam.EcsLite;
using Game.Core.Service;
using Game.Core.Events;

namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Список АКТИВНЫХ ауро-модификаторов стоимости на сущности ИГРОКА — в отличие от CostModifierComponent
    /// (одноразовый ПЕРМАНЕНТНЫЙ «Гиперинфляция»), каждая запись привязана к живому SourceEntity (существу
    /// на столе) и снимается при его смерти/уходе с поля — см. AuraCostModifiers.RemoveBySource, вызывается
    /// из DieSystem/RunLeaveBoardSystem, тем же приёмом, что TrackedBuffs/AppliedBuffs.RemoveTarget.
    /// ExcludeElement — маска цветов (EnumService.Element, [Flags]), которых модификатор НЕ касается
    /// (0 = касается всех цветов). Читается в CostModifierUtil.Effective.
    /// </summary>
    public struct AuraCostModifierComponent
    {
        public List<Record> Items;

        public struct Record
        {
            public int SourceEntity;
            public int Amount;
            public EnumService.Element ExcludeElement;
        }
    }

    public static class AuraCostModifiers
    {
        public static void Add(EcsWorld world, int playerEntity, int sourceEntity, int amount, EnumService.Element excludeElement)
        {
            if (playerEntity < 0 || amount == 0) return;
            var pool = world.GetPool<AuraCostModifierComponent>();
            if (!pool.Has(playerEntity)) pool.Add(playerEntity).Items = new List<AuraCostModifierComponent.Record>();
            ref var c = ref pool.Get(playerEntity);
            c.Items ??= new List<AuraCostModifierComponent.Record>();
            c.Items.Add(new AuraCostModifierComponent.Record
            {
                SourceEntity = sourceEntity,
                Amount = amount,
                ExcludeElement = excludeElement,
            });
        }

        /// <summary>Снять все записи данного источника со ВСЕХ игроков (эффект обычно бьёт по AllPlayers,
        /// а какому игроку в итоге запись принадлежит — не всегда очевидно вызывающему) — звать при смерти/
        /// уходе SourceEntity с поля. Пустой список после удаления не подчищаем (как TrackedBuffs) — Sum
        /// по пустому списку — 0, следующий Add сам создаст записи заново. Публикует CostModifierChangedEvent
        /// САМА (а не полагается на вызывающего — DieSystem/RunLeaveBoardSystem вызывают это заодно с другой
        /// уборкой, легко забыть перерасчёт UI), но только если реально что-то сняла — не спамим событие
        /// на каждую смерть существа, у которого этой ауры не было.</summary>
        public static void RemoveBySource(EcsWorld world, int sourceEntity)
        {
            var pool = world.GetPool<AuraCostModifierComponent>();
            bool removedAny = false;
            foreach (var pe in world.Filter<PlayerComponent>().End())
            {
                if (!pool.Has(pe)) continue;
                int removed = pool.Get(pe).Items?.RemoveAll(r => r.SourceEntity == sourceEntity) ?? 0;
                if (removed > 0) removedAny = true;
            }
            if (removedAny) GameEventBus.Publish(new CostModifierChangedEvent());
        }

        /// <summary>Суммарный ауро-модификатор для карты данного цвета (запись действует, если её
        /// ExcludeElement НЕ содержит цвет карты).</summary>
        public static int Sum(EcsWorld world, int playerEntity, EnumService.Element cardElement)
        {
            var pool = world.GetPool<AuraCostModifierComponent>();
            if (!pool.Has(playerEntity)) return 0;
            var items = pool.Get(playerEntity).Items;
            if (items == null) return 0;

            int sum = 0;
            foreach (var r in items)
                if ((r.ExcludeElement & cardElement) == 0) sum += r.Amount;
            return sum;
        }
    }
}
