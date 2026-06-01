using Game.Core.Shared.Interface;
using Game.Core.Ecs.Components;
using Game.Core.Service;
using Leopotam.EcsLite;

namespace Game.Core.Model.Effect
{
    /// <summary>
    /// Призвать на доску до N карт из указанной зоны (колода / рука / кладбище),
    /// прошедших фильтр. Используется «Работяга» (другой Работяга из колоды),
    /// «Разгром кладбища» (2 существа ≤3 из кладбища), «Харизматичный священник»
    /// (самое дорогое существо из колоды), «Фокус-покус» (2 случайных ≤3) и т.п.
    /// </summary>
    public class SummonFromZoneEffect : AbilityEffect
    {
        public SummonFromZoneSource Source = SummonFromZoneSource.OwnDeck;
        public SummonFromZonePickMode PickMode = SummonFromZonePickMode.First;
        public int Count = 1;

        public int CostMax = -1;
        public int CostMin = 0;
        public int ExactModelId = -1;
        public EnumService.Element RequiredColors;
        public EnumService.Element ForbiddenColors;
        public bool CreatureOnly;
        public bool SpellOnly;
        public bool ExcludeSelf = true;

        public SummonFromZoneEffect() { }
        private SummonFromZoneEffect(SummonFromZoneEffect s)
        {
            Source = s.Source; PickMode = s.PickMode; Count = s.Count;
            CostMax = s.CostMax; CostMin = s.CostMin;
            ExactModelId = s.ExactModelId;
            RequiredColors = s.RequiredColors; ForbiddenColors = s.ForbiddenColors;
            CreatureOnly = s.CreatureOnly; SpellOnly = s.SpellOnly;
            ExcludeSelf = s.ExcludeSelf;
        }

        public override void AddEffect(EcsWorld world, int effectEntity)
        {
            var pool = world.GetPool<SummonFromZoneEffectComponent>();
            if (pool.Has(effectEntity)) return;

            ref var c = ref pool.Add(effectEntity);
            c.Source = Source;
            c.PickMode = PickMode;
            c.Count = Count;
            c.CostMax = CostMax;
            c.CostMin = CostMin;
            c.ExactModelId = ExactModelId;
            c.RequiredColors = RequiredColors;
            c.ForbiddenColors = ForbiddenColors;
            c.CreatureOnly = CreatureOnly;
            c.SpellOnly = SpellOnly;
            c.ExcludeSelf = ExcludeSelf;
        }

        public override IAbilityEffect Clone() => new SummonFromZoneEffect(this);
    }
}
