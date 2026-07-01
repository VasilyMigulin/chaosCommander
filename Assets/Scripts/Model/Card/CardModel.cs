using Game.Core.Service;
using System.Collections.Generic;
using UnityEngine;
using Leopotam.EcsLite;
using Game.Core.Ecs.Components;
using AbilityRuntime = Game.Core.Ability.Ability;
using Game.Core.Shared;             // CardDescriptionFormatter / локализация
using Game.Core.Shared.Interface;   // event-driven способности

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
        public Sprite ArtImage;

        /// <summary>Заполняется ExpansionConfig.Rebuild() при старте.</summary>
        [HideInInspector] public string ExpansionId;

        // Cost to play this card (resource type + amount)
        public EnumService.ResourceType PlayCost;
        public int PlayCostAmount;

        /// <summary>
        /// Токен-карта. Не попадает на кладбище после использования/смерти — просто исчезает.
        /// При инициализации entity автоматически получает TokenTag.
        /// </summary>
        public bool IsToken;

        // ── Модификатор мулигана (Били): «в начале матча начинаете с N карт / замена любого числа» ──
        // Не способность (мулиган идёт ДО тиков способностей) — статичные поля модели; на ините вешаем
        // MulliganModifierComponent, который сканирует InitMulliganSystem. 0/false = нет модификатора.
        public int MulliganStartingHand = 0;
        public bool MulliganUnlimitedReplace = false;

        // Способности карты (Game.Core.Ability). Клонируются на каждую сущность,
        // подписываются на шину; разрешаются через очередь (RunCheckAbilityRulesSystem → AbilityQueue
        // → RunResolveAbilityQueueSystem). Пока пустой у всех ассетов.
        [SerializeReference] public List<AbilityRuntime> RuntimeAbilities = new List<AbilityRuntime>();

        /// <summary>Архетипы существа (работяга/чёрт/...). На ините каждый сам вешает свой ECS-тег
        /// (ICreatureTag.Apply, без switch); матчинг — ArchetypeTargetFilter. Пусто = без архетипа.</summary>
        [SerializeReference] public List<ICreatureTag> Archetypes = new List<ICreatureTag>();
         
        public int Init(EcsWorld world, int playerOwnerEntity, bool isCommander = false)
        {
            int entity = world.NewEntity();

            // Токен-карта
            if (IsToken)
                world.GetPool<TokenTag>().Add(entity);

            // Заполняем CardModelComponent данными из модели
            ref var modelComp = ref world.GetPool<CardModelComponent>().Add(entity);
            modelComp.ModelId     = Id;
            modelComp.ExpansionId = ExpansionId;
            modelComp.CardName    = Name;
            modelComp.Rarity      = Rarity;
            modelComp.Element     = Element;
            modelComp.CardType    = GetCardType();

            // Заполняем CardViewDataComponent — визуальный снэпшот для UI.
            // Имя/описание прогоняем через форматтер: локализация + подстановка *N* + авто-болд
            // ключевых фраз + суффикс длительности для чар. liveValues=null → числа берём из текста
            // (базовые); живой пересчёт под модификаторы — отдельным ре-рендером (см. CardDynamicValues).
            ref var viewData = ref world.GetPool<CardViewDataComponent>().Add(entity);
            string nameKey = CardTextLocalization.NameKey(ExpansionId, Id);
            string descKey = CardTextLocalization.DescKey(ExpansionId, Id);
            viewData.CardName    = CardDescriptionFormatter.FormatName(nameKey, Name);
            viewData.Description = CardDescriptionFormatter.Format(descKey, Description, modelComp.CardType, DescriptionDurationTurns, null);
            viewData.ArtImage    = ArtImage;
            viewData.CardType    = modelComp.CardType;
            viewData.Rarity      = Rarity;
            viewData.Element     = Element;
            viewData.CostType    = PlayCost;
            viewData.CostAmount  = PlayCostAmount;

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
              

            foreach (EnumService.Element flag in System.Enum.GetValues(typeof(EnumService.Element)))
            {
                if ((Element & flag) == 0) continue;

                switch (flag)
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

            // Модификатор мулигана (Били) — статичный маркер, читает InitMulliganSystem.
            if (MulliganStartingHand > 0 || MulliganUnlimitedReplace)
            {
                ref var mm = ref world.GetPool<MulliganModifierComponent>().Add(entity);
                mm.StartingHand = MulliganStartingHand;
                mm.UnlimitedReplace = MulliganUnlimitedReplace;
            }

            // Архетипы — каждый сам вешает свой ECS-тег (см. ICreatureTag).
            if (Archetypes != null)
                foreach (var arch in Archetypes)
                    arch?.Apply(world, entity);

            InitAbilities(world, entity, playerOwnerEntity);

            OnInit(world, entity, isCommander);

            return entity;
        }

        /// <summary>
        /// Клонирует каждую способность под сущность (свой стейт), создаёт ability-сущность с
        /// мостом AbilityRefComponent, инициализирует её (здесь триггеры/условия подписываются на
        /// шину) и записывает id способностей в AbilityContainerComponent карты.
        /// Отписка — на конце сессии (GameEventBus.Clear в EcsRunHandler.Dispose); карта в игре
        /// не удаляется (сжигание = кладбище), поэтому пер-карта teardown не нужен.
        /// </summary>
        void InitAbilities(EcsWorld world, int cardEntity, int playerOwnerEntity)
        {
            ref var container = ref world.GetPool<AbilityContainerComponent>().Add(cardEntity);

            int count = RuntimeAbilities?.Count ?? 0;
            if (count == 0)
            {
                container.AbilityEntities = System.Array.Empty<int>();
                return;
            }

            var abilityEntities = new int[count];
            var refPool = world.GetPool<AbilityRefComponent>();
            for (int i = 0; i < count; i++)
            {
                var clone = (AbilityRuntime)RuntimeAbilities[i].DeepClone();
                int abilityEntity = world.NewEntity();
                refPool.Add(abilityEntity).Ability = clone;
                clone.Init(world, abilityEntity, cardEntity, playerOwnerEntity, i);
                abilityEntities[i] = abilityEntity;
            }
            container.AbilityEntities = abilityEntities;
        }

        protected abstract void OnInit(EcsWorld world, int entityCard, bool isCommander);

        /// <summary>Возвращает CardType для MatchTracker. Переопределяется в наследниках.</summary>
        public virtual EnumService.CardType GetCardType() => EnumService.CardType.Spell;

        /// <summary>Длительность для авто-суффикса описания чар («Действует N ходов» / «До конца матча»).
        /// 0 у не-чар; CardCharmModel отдаёт TurnsAlive.</summary>
        protected virtual int DescriptionDurationTurns => 0;

        // ── Clone ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Создаёт рантайм-копию модели.
        /// Используется при добавлении карты в библиотеку игрока, чтобы
        /// изменения рантайма не затрагивали исходный ScriptableObject-ассет.
        /// </summary>
        public CardModel Clone()
        {
            var copy = (CardModel)MemberwiseClone();
            copy.RuntimeAbilities = new List<AbilityRuntime>(RuntimeAbilities);
            return copy;
        }
    }
}

