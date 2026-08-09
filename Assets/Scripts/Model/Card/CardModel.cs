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

        /// <summary>Показывать эту карту в списке «связанных» инспектора (CardInspectPopup), когда на неё
        /// ссылается другая карта (Source/Card эффектов или пул). Токены показываются всегда (флаг не нужен);
        /// флаг — для ОБЫЧНЫХ карт, порождаемых адресно («чара призывает Бойца на арене»). Карты пулов без
        /// флага не показываются — иначе Фокус-покус вывалил бы весь пул. См. RelatedCardsResolver.</summary>
        public bool ShowAsLinked;

        /// <summary>Обратный флаг: НЕ показывать эту карту как «связанную», даже если её обычно показали бы
        /// (IsToken или ShowAsLinked). Для токенов/карт, которые технически порождаются другой картой, но
        /// светить их в инспекторе не нужно (служебные/спойлерные). Приоритет над IsToken/ShowAsLinked —
        /// см. RelatedCardsResolver.Collect.</summary>
        public bool HideAsLinked;

        /// <summary>НЕБОЕВЫЕ способности — те, что действуют вне боевого цикла: сборка колоды (Сказочник:
        /// отложить 3 карты) и подготовка матча до первого хода (Билли: стартовая рука 4). Отдельное
        /// семейство от RuntimeAbilities, потому что там, где они срабатывают, ECS-мира ещё нет —
        /// см. INonBattleAbility. Пусто = обычная карта.</summary>
        [SerializeReference] public List<INonBattleAbility> NonBattleAbilities = new List<INonBattleAbility>();

        // Способности карты (Game.Core.Ability). Клонируются на каждую сущность,
        // подписываются на шину; разрешаются через очередь (RunCheckAbilityRulesSystem → AbilityQueue
        // → RunResolveAbilityQueueSystem). Пока пустой у всех ассетов.
        [SerializeReference] public List<AbilityRuntime> RuntimeAbilities = new List<AbilityRuntime>();

        /// <summary>Архетипы существа (работяга/чёрт/...). На ините каждый сам вешает свой ECS-тег
        /// (ICreatureTag.Apply, без switch); матчинг — ArchetypeTargetFilter. Пусто = без архетипа.</summary>
        [SerializeReference] public List<ICreatureTag> Archetypes = new List<ICreatureTag>();

        // ── Уровни карты (надстройка): статы/стоимость/способности переопределяются по TierSource ──
        // Каждый CardTier = профиль уровня; активный уровень определяется TierSource (напр. золото по порогам),
        // применяется CardTierSystem (статы/cost дифф-модификаторами) + гейт способностей (AbilityTierGate).
        // None/пусто → обычная карта. Печатные статы/cost базовой модели = уровень 0.
        public TierSource TierSource = TierSource.None;
        [Tooltip("Уровни по возрастанию Threshold. Пусто → обычная карта (без уровней).")]
        [SerializeReference] public List<CardTier> Tiers = new List<CardTier>();
         
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
            // Карта-с-уровнями: снэпшот-описание берём для уровня 0 (свой tier-ключ); дальше CardTierSystem
            // перерендерит под активный уровень и пришлёт CardTierChangedUIEvent.
            string descKey = (Tiers != null && Tiers.Count > 0)
                ? CardTextLocalization.TierDescKey(ExpansionId, Id, 0)
                : CardTextLocalization.DescKey(ExpansionId, Id);
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

            // Cost = эффективное (Base + модификаторы), Base — печатная стоимость (иммутабельна, как у статов).
            switch (PlayCost)
            {
                case EnumService.ResourceType.Gold:
                {
                    ref var c = ref world.GetPool<GoldCostComponent>().Add(entity);
                    c.Base = PlayCostAmount; c.Cost = PlayCostAmount;
                    break;
                }
                case EnumService.ResourceType.Mana:
                {
                    ref var c = ref world.GetPool<ManaCostComponent>().Add(entity);
                    c.Base = PlayCostAmount; c.Cost = PlayCostAmount;
                    break;
                }
                case EnumService.ResourceType.Health:
                {
                    ref var c = ref world.GetPool<HealthCostComponent>().Add(entity);
                    c.Base = PlayCostAmount; c.Cost = PlayCostAmount;
                    break;
                }
            }

            // Правила ПОДГОТОВКИ матча (Билли) → статичный маркер на сущности, который читает
            // InitMulliganSystem. Источник истины — небоевые способности карты.
            var setup = NonBattleRules.Match(NonBattleAbilities);
            if (setup.StartingHand > 0 || setup.UnlimitedReplace)
            {
                ref var mm = ref world.GetPool<MulliganModifierComponent>().Add(entity);
                mm.StartingHand = setup.StartingHand;
                mm.UnlimitedReplace = setup.UnlimitedReplace;
            }

            // Архетипы — каждый сам вешает свой ECS-тег (см. ICreatureTag).
            if (Archetypes != null)
                foreach (var arch in Archetypes)
                    arch?.Apply(world, entity);

            InitAbilities(world, entity, playerOwnerEntity);

            OnInit(world, entity, playerOwnerEntity, isCommander);

            // Карта-с-уровнями: рантайм-состояние (текущий уровень + дифф статов/cost) — считает CardTierSystem.
            if (Tiers != null && Tiers.Count > 0)
                world.GetPool<CardTierComponent>().Add(entity);

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

            var refPool  = world.GetPool<AbilityRefComponent>();
            var gatePool = world.GetPool<AbilityTierGateComponent>();
            var entities = new List<int>();

            // AbilityIndex сквозной и ДЕТЕРМИНИРОВАННЫЙ (общие → потом уровни по порядку) — зеркален на обоих
            // клиентах, чтобы реплей способностей по индексу совпадал.
            int index = 0;

            // 1) Общие способности (без гейта — активны всегда).
            if (RuntimeAbilities != null)
                foreach (var ab in RuntimeAbilities)
                    if (ab != null) entities.Add(InitOneAbility(world, ab, cardEntity, playerOwnerEntity, index++, refPool));

            // 2) Способности уровней (гейт по уровню): инитятся ВСЕ, срабатывают только на своём уровне
            //    (AbilityFire.Mark сверяет CardTierComponent.CurrentTier с Tier).
            if (Tiers != null)
                for (int t = 0; t < Tiers.Count; t++)
                {
                    var tier = Tiers[t];
                    if (tier?.Abilities == null) continue;
                    foreach (var ab in tier.Abilities)
                    {
                        if (ab == null) continue;
                        int e = InitOneAbility(world, ab, cardEntity, playerOwnerEntity, index++, refPool);
                        gatePool.Add(e).Tier = t;
                        entities.Add(e);
                    }
                }

            container.AbilityEntities = entities.ToArray();
        }

        int InitOneAbility(EcsWorld world, AbilityRuntime ability, int cardEntity, int playerOwnerEntity, int index, EcsPool<AbilityRefComponent> refPool)
        {
            var clone = (AbilityRuntime)ability.DeepClone();
            int abilityEntity = world.NewEntity();
            refPool.Add(abilityEntity).Ability = clone;
            clone.Init(world, abilityEntity, cardEntity, playerOwnerEntity, index);
            return abilityEntity;
        }

        /// <summary>Полиморф (RunTransformSystem): снять и диспозить ВСЕ способности карты — клоны
        /// отписываются от шины, ability-сущности удаляются, контейнер снимается. КАВЕАТ: если способность
        /// цели в этот момент стоит в резолв-очереди (напр. её OnDie в том же каскаде), её сущность исчезнет
        /// и резолв тихо отфильтруется — для полиморфа это желаемое («баран не помнит, кем был»).</summary>
        public static void DisposeAbilities(EcsWorld world, int cardEntity)
        {
            var containerPool = world.GetPool<AbilityContainerComponent>();
            if (!containerPool.Has(cardEntity)) return;
            var refPool = world.GetPool<AbilityRefComponent>();
            var entities = containerPool.Get(cardEntity).AbilityEntities;
            if (entities != null)
                foreach (var ae in entities)
                {
                    if (refPool.Has(ae)) refPool.Get(ae).Ability?.Dispose();
                    world.DelEntity(ae);
                }
            containerPool.Del(cardEntity);
        }

        /// <summary>Полиморф: инициализировать способности ЭТОЙ модели на СУЩЕСТВУЮЩЕЙ сущности карты
        /// (контейнер пересоздаётся; предварительно снять старые через DisposeAbilities).</summary>
        public void InitAbilitiesFor(EcsWorld world, int cardEntity, int playerOwnerEntity)
            => InitAbilities(world, cardEntity, playerOwnerEntity);

        /// <summary>Полиморф: повторный прогон ТИПОВОГО инита модели (OnInit — статы/теги/вьюреф существа,
        /// таймеры) на СУЩЕСТВУЮЩЕЙ сущности. Вызывающий (RunTransformSystem) ОБЯЗАН заранее снять всё, что
        /// OnInit кладёт через Add (CreatureTag/ViewRef/Attack/Health/Speed/таймер/SummonVfx) — иначе Add кинет.
        /// Через базовый класс — чтобы сборка Systems не ссылалась на сборки конкретных типов карт.</summary>
        public void ReapplyTypeInit(EcsWorld world, int entity, int playerOwnerEntity)
            => OnInit(world, entity, playerOwnerEntity, isCommander: false);

        protected abstract void OnInit(EcsWorld world, int entityCard, int playerOwnerEntity, bool isCommander);

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

