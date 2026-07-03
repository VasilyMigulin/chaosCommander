using System;
using Game.Core.Ecs.Components;
using Leopotam.EcsLite;

namespace Game.Core.Ability
{
    // ─────────────────────────────────────────────────────────────────────────
    // КЛАСТЕР 5 (соседи) — урон СОСЕДЯМ цели (Manhattan-1 на ТОЙ ЖЕ стороне доски). Композиция в одной
    // способности: Selected + эффект(ы) по target. СИНК: детерминирован по target (target в ключах).
    // ─────────────────────────────────────────────────────────────────────────

    // === helper === урон существам, соседним с центром (та же сторона + Manhattan-1). Центр не задевает.
    static class AdjacencyUtil
    {
        public static void DamageNeighbors(EcsWorld world, int sourceCard, int center, int amount)
        {
            if (center < 0 || amount <= 0) return;
            var posPool = world.GetPool<BoardPositionComponent>();
            if (!posPool.Has(center)) return;
            var cp = posPool.Get(center);
            int side = cp.OwnerId, row = cp.Row, col = cp.Col;

            var dmgPool = world.GetPool<TakeDamageEvent>();
            foreach (var e in world.Filter<CreatureTag>().Inc<BoardTag>().Inc<BoardPositionComponent>().Exc<DeadTag>().End())
            {
                if (e == center) continue;
                var p = posPool.Get(e);
                if (p.OwnerId != side) continue;
                if (Math.Abs(p.Row - row) + Math.Abs(p.Col - col) != 1) continue;
                if (!dmgPool.Has(e)) dmgPool.Add(e);
                ref var d = ref dmgPool.Get(e);
                d.Amount += amount;
                d.Attacker = sourceCard;
            }
        }
    }

    // === class (OOP) === Урон существам, СОСЕДНИМ с целью (Бомбануть: «2 ближайшим»). Цель не задевает.
    [Serializable]
    public sealed class DealDamageToNeighborsEffect : EffectBase
    {
        public override Game.Core.Shared.Interface.AiEffectRole AiRole => Game.Core.Shared.Interface.AiEffectRole.Damage;
        public int Amount = 1;

        public override void Apply(EcsWorld world, int cardEntity, int target)
            => AdjacencyUtil.DamageNeighbors(world, cardEntity, target, Amount);
    }

    // === class (OOP) === Урон цели; ЕСЛИ цель от него погибнет — урон SplashAmount соседям (Зажигалка).
    // Летальность ПРЕДСКАЗЫВАЕМ синхронно (Current <= Amount: урон = Current-=Amount, гибель при <=0), потому
    // что соседей надо взять ДО смерти цели (после смерти позиция снимается). СИНК: детерминирован по target.
    [Serializable]
    public sealed class DealDamageWithLethalSplashEffect : EffectBase
    {
        public override Game.Core.Shared.Interface.AiEffectRole AiRole => Game.Core.Shared.Interface.AiEffectRole.Damage;
        public int Amount = 1;        // урон цели
        public int SplashAmount = 5;  // урон соседям, если цель погибнет

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (target < 0) return;
            var hpPool = world.GetPool<HealthComponent>();
            if (!hpPool.Has(target)) return;

            bool lethal = hpPool.Get(target).Current <= Amount;             // предсказываем гибель
            if (lethal) AdjacencyUtil.DamageNeighbors(world, cardEntity, target, SplashAmount);  // соседей берём ДО смерти

            var dmgPool = world.GetPool<TakeDamageEvent>();                 // урон самой цели
            if (!dmgPool.Has(target)) dmgPool.Add(target);
            ref var d = ref dmgPool.Get(target);
            d.Amount += Amount;
            d.Attacker = cardEntity;
        }
    }
}
