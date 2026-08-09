using System;
using Game.Core.Ecs.Components;
using Leopotam.EcsLite;

namespace Game.Core.Ability
{
    // ─────────────────────────────────────────────────────────────────────────
    // Существо-цель ДОЛЖНО броситься в атаку на ближайшего врага — бесплатно по скорости, сверх обычного
    // лимита атак за ход (Позвать стражу: призванные стражи «бросаются в атаку»). Сам поиск/маршрут/атака
    // считает ForceSeekAttackSystem (Ecs.Systems-сборка, там же BoardNav) — этот эффект лишь помечает
    // намерение тегом, т.к. Ability-сборка не ссылается на Ecs.Systems (как и остальные эффекты, движение
    // по борду эффекты никогда не считают сами). Используется как SummonModifier у SpawnOnBoardEffect
    // (FillRowWithCardEffect и т.п.) — target там = порождённая сущность.
    // ─────────────────────────────────────────────────────────────────────────
    [Serializable]
    public sealed class ForceAttackEffect : EffectBase
    {
        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (target < 0) return;
            if (!world.GetPool<CreatureTag>().Has(target) || !world.GetPool<BoardTag>().Has(target)) return;
            var tagPool = world.GetPool<ForceSeekAttackTag>();
            if (!tagPool.Has(target)) tagPool.Add(target);
        }
    }
}
