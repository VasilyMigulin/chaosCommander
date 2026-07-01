using System.Collections.Generic;
using System.Linq;
using Game.Core.Ecs.Components;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;

namespace Game.Core.Ability
{
    // === class (OOP) ===
    /// <summary>
    /// СОСТАВНАЯ (цепочечная) способность: обычные Triggers/Rules + упорядоченный список стадий.
    /// Каждая стадия (ChainStage) имеет свой таргетинг и свои эффекты; выполняются по порядку, между
    /// ними мир оседает, результаты (KilledCount) переносятся дальше. Резолв ведёт RunChainSystem.
    ///
    /// Пример «Окропить»: стадия 0 — Field(All, без жёлтого) → DealDamage(2); стадия 1 — NonTarget →
    /// GainRandomCardFromPool(Yellow,Spell, CountSource=ChainKilled).
    /// </summary>
    public sealed class AbilityChain : Ability
    {
        public List<ChainStage> Stages = new();

        protected override void OnInit(EcsWorld world, int abilityEntity)
        {
            if (Stages == null || Stages.Count == 0) return;

            ref var owner = ref world.GetPool<AbilityOwnerComponent>().Get(abilityEntity);
            int card = owner.CardEntity;
            int player = owner.PlayerEntity;

            // Инитим эффекты всех стадий (внутри них — условия).
            foreach (var stage in Stages)
                if (stage?.Effects != null)
                    foreach (var eff in stage.Effects)
                        eff?.Init(world, card, player);

            world.GetPool<AbilityChainComponent>().Add(abilityEntity).Stages = Stages.ToArray();
        }

        protected override void OnDispose()
        {
            if (Stages == null) return;
            foreach (var stage in Stages)
                if (stage?.Effects != null)
                    foreach (var eff in stage.Effects)
                        eff?.Dispose();
        }
    }
}
