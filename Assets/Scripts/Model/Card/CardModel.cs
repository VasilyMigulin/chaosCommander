using Game.Core.Service;
using System.Collections.Generic;
using UnityEngine;
using Leopotam.EcsLite;
using Game.Core.Ecs.Components;
using Game.Core.Model.Ability; 

namespace Game.Core.Model.Card
{
    /// <summary>
    /// Base card data model. Instances are ScriptableObject-like data containers
    /// created at design-time and used to spawn runtime ECS entities.
    /// </summary>
    public abstract class CardModel : Model
    {
        public EnumService.Rarity Rarity;
        public EnumService.Element Element;
        public Sprite Icon;

        /// <summary>Заполняется ExpansionConfig.Rebuild() при старте.</summary>
        [HideInInspector] public string ExpansionId;

        // Cost to play this card (resource type + amount)
        public EnumService.ResourceType PlayCost;
        public int PlayCostAmount;

        // Ability descriptors attached to this card (populated by factory / design data)
        [SerializeReference] public List<Ability.Ability> Abilities = new List<Ability.Ability>();

        public void Init(EcsWorld world)
        {
            InitAndGetEntity(world);
        }

        public int InitAndGetEntity(EcsWorld world)
        {
            int entity = world.NewEntity();

            // Заполняем CardModelComponent данными из модели
            ref var modelComp = ref world.GetPool<CardModelComponent>().Add(entity);
            modelComp.ModelId     = Id;
            modelComp.ExpansionId = ExpansionId;
            modelComp.CardName    = Name;
            modelComp.Rarity      = Rarity;
            modelComp.Element     = Element;
            modelComp.CardType    = GetCardType();

            ref var abiliyContainerComp = ref world.GetPool<AbilityContainerComponent>().Add(entity);

            switch (Rarity)
            {
                case EnumService.Rarity.Common:
                    world.GetPool<CommonTag>().Add(entity);
                    break;
                case EnumService.Rarity.Rare:
                    world.GetPool<RareTag>().Add(entity); 
                    break;
                case EnumService.Rarity.Epic:
                    world.GetPool<EpicTag>().Add(entity); 
                    break;
                case EnumService.Rarity.Legendary:
                    world.GetPool<LegendaryTag>().Add(entity);
                    break;
                case EnumService.Rarity.Exotic:
                    world.GetPool<ExoticTag>().Add(entity); 
                    break;
            }

            switch (Element)
            {
                case EnumService.Element.Red:
                    world.GetPool<RedTag>().Add(entity);
                    break;
                case EnumService.Element.Blue:
                    world.GetPool<BlueTag>().Add(entity);
                    break;
                case EnumService.Element.Green:
                    world.GetPool<GreenTag>().Add(entity);
                    break;
                case EnumService.Element.Yellow:
                    world.GetPool<YellowTag>().Add(entity);
                    break;
                case EnumService.Element.White:
                    world.GetPool<WhiteTag>().Add(entity);
                    break;
                case EnumService.Element.Black:
                    world.GetPool<BlackTag>().Add(entity);
                    break;
            }

            switch (PlayCost)
            {
                case EnumService.ResourceType.Gold:
                    world.GetPool<GoldCostComponent>().Add(entity).Cost = PlayCostAmount;
                    break;
                case EnumService.ResourceType.Mana:
                    world.GetPool<ManaCostComponent>().Add(entity).Cost = PlayCostAmount;
                    break;
                case EnumService.ResourceType.Health:
                    world.GetPool<HealthCostComponent>().Add(entity).Cost = PlayCostAmount;
                    break;
            }

            List<int> abilityEntities = new List<int>();

            foreach (var ability in Abilities)
            {
                int abilityEntity = ability.Init(world, entity);
                abilityEntities.Add(abilityEntity);
            }

            abiliyContainerComp.AbilityEntities = abilityEntities.ToArray();

            OnInit(world, entity);

            return entity;
        }

        protected abstract void OnInit(EcsWorld world, int entityCard);

        /// <summary>Возвращает CardType для MatchTracker. Переопределяется в наследниках.</summary>
        public virtual EnumService.CardType GetCardType() => EnumService.CardType.Spell;

        // ── Clone ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Создаёт рантайм-копию модели.
        /// Используется при добавлении карты в библиотеку игрока, чтобы
        /// изменения рантайма не затрагивали исходный ScriptableObject-ассет.
        /// </summary>
        public CardModel Clone()
        {
            var copy = (CardModel)MemberwiseClone();
            copy.Abilities = new List<Ability.Ability>(Abilities);
            return copy;
        }
    }
}

