using System.Collections.Generic;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Configs;
using Game.Core.Model.Card;
using Game.Core.Service;
using Game.Core.Shared;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// ПОЛИМОРФ (TransformCardEvent от TransformEffect): мутирует существо-цель в карту exp/id НА МЕСТЕ —
    /// та же сущность, та же клетка, тот же NetworkEntityKey. Меняются: модель/имя/описание, статы (полный
    /// сброс — БАФФЫ СГОРАЮТ, классика полиморфа), кост, способности (старые Dispose+DelEntity, новые
    /// инитятся с ФИНАЛЬНОГО владельца — Rebind не нужен), архетипы, теги редкости/цвета/токена, таймеры,
    /// вьюха (старый инстанс уничтожается, ViewSpawnedTag снимается → SpawnCreatureViewSystem пересоздаёт
    /// на той же клетке с PlaySummon-«пуфом»). NewOwnerId >= 0 → смена контроля НА МЕСТЕ (как TakeControl:
    /// OwnerComponent + своп клиент-относительных Own/Enemy-тегов; позицию/сторону клетки НЕ трогаем).
    /// СИНК ДАРОМ: событие публикует эффект, ре-ранящийся на ОБОИХ клиентах; модель по ассету → зеркально.
    /// </summary>
    public sealed class RunTransformSystem : IEcsInitSystem, IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsCustomInject<CardConfig> _cardConfig = default;

        readonly Queue<TransformCardEvent> _pending = new Queue<TransformCardEvent>();

        public void Init(IEcsSystems systems)
            => GameEventBus.Subscribe<TransformCardEvent>(e => _pending.Enqueue(e));

        public void Run(IEcsSystems systems)
        {
            while (_pending.Count > 0) Mutate(_pending.Dequeue());
        }

        void Mutate(TransformCardEvent e)
        {
            var world = _world.Value;
            int target = e.TargetEntity;
            if (target < 0) return;
            if (!world.GetPool<CardModelComponent>().Has(target)) return;            // сущность уже умерла/ушла
            if (world.GetPool<DeadTag>().Has(target)) return;                        // труп не полиморфим
            // Страховка (эффект гейтит раньше) — но с логом: молчаливый выход тут означал бы, что событие
            // дошло в обход гейта эффекта, а это уже настоящая поломка, и её надо видеть.
            if (world.GetPool<CommanderTag>().Has(target))
            {
                UnityEngine.Debug.LogWarning($"[Transform] entity={target} — командир дошёл до системы в обход гейта эффекта → skip");
                return;
            }

            var model = _cardConfig.Value.Get(e.ExpansionId, e.CardId)?.CardData;
            if (model == null || model.GetCardType() != EnumService.CardType.Creature)
            {
                UnityEngine.Debug.LogWarning($"[Transform] exp={e.ExpansionId} id={e.CardId} — не существо/не найдено → skip");
                return;
            }

            ref var oldModel = ref world.GetPool<CardModelComponent>().Get(target);
            var oldRarity  = oldModel.Rarity;
            var oldElement = oldModel.Element;

            // ── Смена контроля (Чертовщина) — ДО инита способностей, чтобы они сразу служили новому хозяину.
            var ownerPool = world.GetPool<OwnerComponent>();
            int finalOwnerId = ownerPool.Has(target) ? ownerPool.Get(target).OwnerId : -1;
            if (e.NewOwnerId >= 0 && ownerPool.Has(target) && finalOwnerId != e.NewOwnerId)
            {
                // Память об изначальном владельце — как у TakeControlEffect (первый владелец переживает перехваты).
                var origPool = world.GetPool<OriginalOwnerComponent>();
                if (!origPool.Has(target)) origPool.Add(target).OwnerId = finalOwnerId;

                ownerPool.Get(target).OwnerId = e.NewOwnerId;
                finalOwnerId = e.NewOwnerId;

                var ownTag = world.GetPool<OwnCardTag>();
                var enemyTag = world.GetPool<EnemyCardTag>();
                bool wasOwn = ownTag.Has(target);
                if (wasOwn) { ownTag.Del(target); if (!enemyTag.Has(target)) enemyTag.Add(target); }
                else        { if (enemyTag.Has(target)) enemyTag.Del(target); if (!ownTag.Has(target)) ownTag.Add(target); }
            }
            int finalPlayerEntity = AbilityRebindUtil.FindPlayerEntity(world, finalOwnerId);

            // ── Старая начинка долой: способности (клоны отписываются от шины), архетипы, теги модели.
            // СНАЧАЛА откатываем ауры, выданные ЭТИМ существом: после DisposeAbilities эффекты отписаны от
            // CreatureDiedEvent, а смерти тут и не происходит — баффы остались бы на целях навсегда
            // (баг 2026-08-02: полиморф «Начальника смены» оставлял «Работяге» повышенные статы). ДВА
            // параллельных трекинга ауры в проекте (AddBuffEffect{Tracked} → TrackedBuffsComponent,
            // ApplyTrackedBuffEffect/«Начальник смены» → AppliedBuffsComponent) — откатываем ОБА, иначе
            // полиморф через второй механизм (напр. Чертовщина) воспроизводит тот же баг заново.
            TrackedBuffs.RevertAll(world, target);
            AppliedBuffs.RevertAll(world, target);
            CardModel.DisposeAbilities(world, target);
            if (world.GetPool<ArchetypeComponent>().Has(target)) world.GetPool<ArchetypeComponent>().Del(target);
            SetRarityTag(world, target, oldRarity, on: false);
            SetElementTags(world, target, oldElement, on: false);

            // ── Токен-флаг по новой модели.
            var tokenPool = world.GetPool<TokenTag>();
            if (tokenPool.Has(target) && !model.IsToken) tokenPool.Del(target);
            if (!tokenPool.Has(target) && model.IsToken) tokenPool.Add(target);

            // ── Кост: старые компоненты долой (вместе со стеками модификаторов), новый — печатный по модели.
            var gold = world.GetPool<GoldCostComponent>();   if (gold.Has(target)) gold.Del(target);
            var mana = world.GetPool<ManaCostComponent>();   if (mana.Has(target)) mana.Del(target);
            var hpc  = world.GetPool<HealthCostComponent>(); if (hpc.Has(target))  hpc.Del(target);
            switch (model.PlayCost)
            {
                case EnumService.ResourceType.Gold:   { ref var c = ref gold.Add(target); c.Base = model.PlayCostAmount; c.Cost = model.PlayCostAmount; break; }
                case EnumService.ResourceType.Mana:   { ref var c = ref mana.Add(target); c.Base = model.PlayCostAmount; c.Cost = model.PlayCostAmount; break; }
                case EnumService.ResourceType.Health: { ref var c = ref hpc.Add(target);  c.Base = model.PlayCostAmount; c.Cost = model.PlayCostAmount; break; }
            }

            // ── Снять ВСЁ, что кладёт типовой инит модели (ReapplyTypeInit ниже прогонит его заново с новой
            //    моделью). Del статов стирает и стеки модификаторов — баффы/дебаффы СГОРАЮТ (классика полиморфа).
            var atkPool = world.GetPool<AttackComponent>();          if (atkPool.Has(target)) atkPool.Del(target);
            var hpPool  = world.GetPool<HealthComponent>();          if (hpPool.Has(target)) hpPool.Del(target);
            var spPool  = world.GetPool<SpeedComponent>();           if (spPool.Has(target)) spPool.Del(target);
            var creaturePool = world.GetPool<CreatureTag>();         if (creaturePool.Has(target)) creaturePool.Del(target);
            var timerPool = world.GetPool<CreatureTimerComponent>(); if (timerPool.Has(target)) timerPool.Del(target);
            var vfxPool = world.GetPool<SummonVfxComponent>();       if (vfxPool.Has(target)) vfxPool.Del(target);

            // Вьюха: старый инстанс сносим и снимаем ViewRef/ViewSpawnedTag → типовой инит даст новый префаб,
            // SpawnCreatureViewSystem пересоздаст вид на ТОЙ ЖЕ клетке (PlaySummon-«пуф» — визуал полиморфа).
            var viewPool = world.GetPool<ViewRefComponent>();
            if (viewPool.Has(target))
            {
                ref var vr = ref viewPool.Get(target);
                if (vr.View != null) UnityEngine.Object.Destroy(vr.View);
                viewPool.Del(target);
            }
            var spawnedPool = world.GetPool<ViewSpawnedTag>();
            if (spawnedPool.Has(target)) spawnedPool.Del(target);

            var attacksUsed = world.GetPool<AttacksUsedComponent>();
            if (attacksUsed.Has(target)) attacksUsed.Get(target).Value = 0;   // «новое» существо ещё не било

            // ── Уровни/мулиган — по новой модели.
            var tierPool = world.GetPool<CardTierComponent>();
            if (tierPool.Has(target)) tierPool.Del(target);
            if (model.Tiers != null && model.Tiers.Count > 0) tierPool.Add(target);

            var mullPool = world.GetPool<MulliganModifierComponent>();
            if (mullPool.Has(target)) mullPool.Del(target);   // на поле мулиган не актуален

            // ── Идентичность + вью-данные.
            oldModel.ModelId = model.Id;
            oldModel.ExpansionId = model.ExpansionId;
            oldModel.CardName = model.Name;
            oldModel.Rarity = model.Rarity;
            oldModel.Element = model.Element;
            oldModel.CardType = EnumService.CardType.Creature;
            SetRarityTag(world, target, model.Rarity, on: true);
            SetElementTags(world, target, model.Element, on: true);

            var viewDataPool = world.GetPool<CardViewDataComponent>();
            if (viewDataPool.Has(target))
            {
                ref var vd = ref viewDataPool.Get(target);
                string nameKey = CardTextLocalization.NameKey(model.ExpansionId, model.Id);
                string descKey = (model.Tiers != null && model.Tiers.Count > 0)
                    ? CardTextLocalization.TierDescKey(model.ExpansionId, model.Id, 0)
                    : CardTextLocalization.DescKey(model.ExpansionId, model.Id);
                vd.CardName    = CardDescriptionFormatter.FormatName(nameKey, model.Name);
                vd.Description = CardDescriptionFormatter.Format(descKey, model.Description, EnumService.CardType.Creature, 0, null);
                vd.ArtImage    = model.ArtImage;
                vd.CardType    = EnumService.CardType.Creature;
                vd.Rarity      = model.Rarity;
                vd.Element     = model.Element;
                vd.CostType    = model.PlayCost;
                vd.CostAmount  = model.PlayCostAmount;
                vd.IsCommander = false;
                // IsCreature/Attack/MaxHealth/Speed в viewData допишет типовой инит модели (ReapplyTypeInit ниже).
            }

            // ── Новая начинка: типовой инит модели (статы/CreatureTag/ViewRef с новым префабом/таймер/SummonVfx
            //    + статы в viewData), архетипы, способности (Init сразу с ФИНАЛЬНЫМ владельцем — Rebind не нужен).
            model.ReapplyTypeInit(world, target, finalPlayerEntity);
            if (model.Archetypes != null)
                foreach (var arch in model.Archetypes)
                    arch?.Apply(world, target);
            model.InitAbilitiesFor(world, target, finalPlayerEntity);

            // ── UI: HP-дисплей/статы обновить (вьюха и так пересоздастся, но событие держит дисплеи честными).
            GameEventBus.Publish(new CreatureHealthChangedEvent { CreatureEntity = target });
            UnityEngine.Debug.Log($"[Transform] entity={target} → {model.ExpansionId}/{model.Id} '{model.Name}' owner={finalOwnerId}");
        }

        static void SetRarityTag(EcsWorld world, int e, EnumService.Rarity rarity, bool on)
        {
            switch (rarity)
            {
                case EnumService.Rarity.Common:    Toggle<CommonTag>(world, e, on); break;
                case EnumService.Rarity.Rare:      Toggle<RareTag>(world, e, on); break;
                case EnumService.Rarity.Epic:      Toggle<EpicTag>(world, e, on); break;
                case EnumService.Rarity.Legendary: Toggle<LegendaryTag>(world, e, on); break;
                case EnumService.Rarity.Exotic:    Toggle<ExoticTag>(world, e, on); break;
            }
        }

        static void SetElementTags(EcsWorld world, int e, EnumService.Element element, bool on)
        {
            if ((element & EnumService.Element.Red)    != 0) Toggle<RedTag>(world, e, on);
            if ((element & EnumService.Element.Blue)   != 0) Toggle<BlueTag>(world, e, on);
            if ((element & EnumService.Element.Green)  != 0) Toggle<GreenTag>(world, e, on);
            if ((element & EnumService.Element.Yellow) != 0) Toggle<YellowTag>(world, e, on);
            if ((element & EnumService.Element.White)  != 0) Toggle<WhiteTag>(world, e, on);
            if ((element & EnumService.Element.Black)  != 0) Toggle<BlackTag>(world, e, on);
        }

        static void Toggle<T>(EcsWorld world, int e, bool on) where T : struct
        {
            var pool = world.GetPool<T>();
            if (on) { if (!pool.Has(e)) pool.Add(e); }
            else    { if (pool.Has(e)) pool.Del(e); }
        }
    }
}
