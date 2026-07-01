using System;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Service;
using Leopotam.EcsLite;

namespace Game.Core.Ecs.Systems
{
    // === helper === возврат стоимости каста игроку (при отмене размещения/таргетинга).
    // Парный к оплате в RunCastRouterSystem. player — сущность игрока-владельца.
    static class CastCost
    {
        public static void Refund(EcsWorld world, int card, int player)
        {
            var gold = world.GetPool<GoldCostComponent>();
            if (gold.Has(card)) { Add(world, player, EnumService.ResourceType.Gold, gold.Get(card).Cost); return; }

            var mana = world.GetPool<ManaCostComponent>();
            if (mana.Has(card)) { Add(world, player, EnumService.ResourceType.Mana, mana.Get(card).Cost); return; }

            var health = world.GetPool<HealthCostComponent>();
            if (health.Has(card)) { Add(world, player, EnumService.ResourceType.Health, health.Get(card).Cost); }
        }

        static void Add(EcsWorld world, int player, EnumService.ResourceType type, int amount)
        {
            switch (type)
            {
                case EnumService.ResourceType.Gold:
                {
                    var pool = world.GetPool<GoldComponent>();
                    if (!pool.Has(player)) return;
                    ref var r = ref pool.Get(player);
                    r.Current = Math.Min(r.Current + amount, r.Max);
                    Publish(world, player, type, r.Current, r.Max);
                    break;
                }
                case EnumService.ResourceType.Mana:
                {
                    var pool = world.GetPool<ManaComponent>();
                    if (!pool.Has(player)) return;
                    ref var r = ref pool.Get(player);
                    r.Current = Math.Min(r.Current + amount, r.Max);
                    Publish(world, player, type, r.Current, r.Max);
                    break;
                }
                case EnumService.ResourceType.Health:
                {
                    var pool = world.GetPool<HealthComponent>();
                    if (!pool.Has(player)) return;
                    ref var r = ref pool.Get(player);
                    r.Current = Math.Min(r.Current + amount, r.Max);
                    Publish(world, player, type, r.Current, r.Max);
                    break;
                }
            }
        }

        static void Publish(EcsWorld world, int player, EnumService.ResourceType type, int newValue, int maxValue)
        {
            var pp = world.GetPool<PlayerComponent>();
            GameEventBus.Publish(new ResourceChangedEvent
            {
                isLocalPlayer = pp.Has(player) && pp.Get(player).IsLocalPlayer,
                Type     = type,
                NewValue = newValue,
                MaxValue = maxValue,
            });
        }
    }
}
