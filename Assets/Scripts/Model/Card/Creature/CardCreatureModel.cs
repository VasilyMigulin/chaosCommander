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

        protected override void OnInit(EcsWorld world, int entityCard)
        {
            world.GetPool<AttackComponent>().Add(entityCard).Value = Attack;
            world.GetPool<HealthComponent>().Add(entityCard).Max = MaxHealth;

            world.GetPool<CreatureTag>().Add(entityCard);

            if (IsCommander)
                world.GetPool<CommanderTag>().Add(entityCard);

            world.GetPool<ViewRefComponent>().Add(entityCard).View = ViewPrefab;
        }
    }
}

