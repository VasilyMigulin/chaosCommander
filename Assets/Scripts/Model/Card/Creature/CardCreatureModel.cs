using Game.Core.Ecs.Components;
using Leopotam.EcsLite;
using UnityEngine;

namespace Game.Core.Model.Card.Creature
{
    /// <summary>
    /// Data model for creature cards.
    /// Played using Gold resource. Occupies a board cell.
    /// </summary>
    public class CardCreatureModel : CardModel
    {
        public int MaxHealth;
        public int Attack;

        /// <summary>Actions per turn (move + attack budget).</summary>
        public int Speed;

        /// <summary>Prefab spawned on the board when this creature enters play.</summary>
        public GameObject ViewPrefab;

        /// <summary>Назначается сервисом сборки колоды когда эта карта выбрана командиром.</summary>
        [HideInInspector] public bool IsCommander;

        public override Game.Core.Service.EnumService.CardType GetCardType() => Game.Core.Service.EnumService.CardType.Creature;

        protected override void OnInit(EcsWorld world, int entityCard, bool isCommander)
        {
            ref var atk = ref world.GetPool<AttackComponent>().Add(entityCard);
            atk.Value = Attack;
            atk.Base  = Attack;

            ref var hp = ref world.GetPool<HealthComponent>().Add(entityCard);
            hp.Max     = MaxHealth;
            hp.BaseMax = MaxHealth;
            hp.Current = MaxHealth;

            world.GetPool<CreatureTag>().Add(entityCard);

            if (isCommander)
                world.GetPool<CommanderTag>().Add(entityCard);

            world.GetPool<ViewRefComponent>().Add(entityCard).Prefab = ViewPrefab;

            ref var speed = ref world.GetPool<SpeedComponent>().Add(entityCard);
            speed.Max       = Speed;
            speed.Remaining = Speed;

            if (world.GetPool<CardViewDataComponent>().Has(entityCard))
            {
                ref var viewData    = ref world.GetPool<CardViewDataComponent>().Get(entityCard);
                viewData.IsCreature = true;
                viewData.Attack     = Attack;
                viewData.MaxHealth  = MaxHealth;
                viewData.Speed      = Speed;
                viewData.IsCommander = isCommander;
            }
        }
    }
}

