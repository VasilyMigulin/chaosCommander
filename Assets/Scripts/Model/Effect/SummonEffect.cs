using Game.Core.Shared.Interface;
using Game.Core.Ecs.Components;
using Leopotam.EcsLite;

namespace Game.Core.Model.Effect
{
    /// <summary>
    /// Эффект призыва: разместить указанную карту/токен на доске владельца
    /// способности. Порядок размещения — чередование L/R от источника
    /// (существо на доске) либо от центра аватара (чары/заклинание).
    /// При отсутствии места: обычные карты уходят в кладбище, токены исчезают.
    /// </summary>
    public class SummonEffect : AbilityEffect
    {
        public string ExpansionId;
        public int CardId;
        public int Count = 1;
        public bool FillRow;
        public bool IsToken;
        /// <summary>≥ 0 — Count берётся из MatchCounter (счётчик ModelId у владельца).</summary>
        public int CountFromCounterModelId = -1;

        public SummonEffect() { }
        public SummonEffect(string expansionId, int cardId, int count = 1, bool fillRow = false, bool isToken = false)
        {
            ExpansionId = expansionId;
            CardId = cardId;
            Count = count;
            FillRow = fillRow;
            IsToken = isToken;
        }

        private SummonEffect(SummonEffect source)
        {
            ExpansionId = source.ExpansionId;
            CardId = source.CardId;
            Count = source.Count;
            FillRow = source.FillRow;
            IsToken = source.IsToken;
            CountFromCounterModelId = source.CountFromCounterModelId;
        }

        public override void AddEffect(EcsWorld world, int effectEntity)
        {
            var pool = world.GetPool<SummonEffectComponent>();
            if (pool.Has(effectEntity)) return;

            ref var comp = ref pool.Add(effectEntity);
            comp.ExpansionId = ExpansionId;
            comp.CardId = CardId;
            comp.Count = Count;
            comp.FillRow = FillRow;
            comp.IsToken = IsToken;
            comp.CountFromCounterModelId = CountFromCounterModelId;
        }

        public override IAbilityEffect Clone() => new SummonEffect(this);
    }
}
