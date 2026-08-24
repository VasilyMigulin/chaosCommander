using System;
using Game.Core.Events;
using Game.Core.Ecs.Components;
using Leopotam.EcsLite;
using Game.Core.Service;

namespace Game.Core.Ability
{
    // ─────────────────────────────────────────────────────────────────────────
    // БИБЛИОТЕКА УСЛОВИЙ — реактивная готовность эффекта (IEffect.Condition).
    // event-driven: подписка с owner=this, Changed дёргается при смене IsReady.
    // Для простых «можно ли сейчас» предпочитайте ПРАВИЛА (AbilityRules) — они
    // pull-стиль и надёжнее; условия нужны, когда готовность копится во времени.
    // ─────────────────────────────────────────────────────────────────────────

    // === class (OOP) === Готово, пока HP существа-источника ниже порога.
    // Реагирует на урон (CreatureDamagedEvent). Лечение/бафф событий не шлют —
    // пересчёт по ним не реактивен (приемлемо: порог обычно «ранен → бонус»).
    [Serializable]
    public sealed class SourceHealthBelowCondition : ICondition
    {
        public int Threshold = 2;

        public bool IsReady { get; private set; }
        public event Action Changed;

        AbilityContext _ctx;

        public void Init(AbilityContext ctx)
        {
            _ctx = ctx;
            GameEventBus.Subscribe<CreatureDamagedEvent>(this, OnDamaged);
            Recompute();
        }

        void OnDamaged(CreatureDamagedEvent e)
        {
            if (e.CreatureEntity != _ctx.CardEntity) return;
            Recompute();
        }

        void Recompute()
        {
            var hp = _ctx.World.GetPool<HealthComponent>();
            bool now = hp.Has(_ctx.CardEntity) && hp.Get(_ctx.CardEntity).Current < Threshold;
            if (now == IsReady) return;
            IsReady = now;
            Changed?.Invoke();
        }

        public void Dispose() => GameEventBus.UnsubscribeAll(this);
    }

    // === class (OOP) === Готово, пока в КОЛОДЕ владельца нет существ (архетип «без существ»: Пустой
    // фолиант «если в колоде нет существ — возьмите ещё 2»). Пересчёт на событиях, меняющих состав колоды
    // (добор/розыгрыш/генерация/призыв из колоды) + страховочно на старте хода. Скан по зеркальным тегам →
    // IsReady одинаков на обоих клиентах.
    [Serializable]
    public sealed class NoCreaturesInDeckCondition : ICondition
    {
        public bool IsReady { get; private set; }
        public event Action Changed;

        AbilityContext _ctx;

        public void Init(AbilityContext ctx)
        {
            _ctx = ctx;
            GameEventBus.Subscribe<CardDrawnEvent>(this, _ => Recompute());
            GameEventBus.Subscribe<CardGeneratedEvent>(this, _ => Recompute());
            GameEventBus.Subscribe<CardPlayedEvent>(this, _ => Recompute());
            GameEventBus.Subscribe<CreatureInvokedEvent>(this, _ => Recompute());
            GameEventBus.Subscribe<TurnStartedEvent>(this, _ => Recompute());
            Recompute();
        }

        void Recompute()
        {
            int ownerId = RuleUtil.OwnerId(_ctx.World, _ctx.CardEntity);
            bool now = ownerId >= 0 && !RuleUtil.DeckHasCreatures(_ctx.World, ownerId);
            if (now == IsReady) return;
            IsReady = now;
            Changed?.Invoke();
        }

        public void Dispose() => GameEventBus.UnsubscribeAll(this);
    }

    // === class (OOP) === Готово, пока ВСЕ карты владельца в КОЛОДЕ (кроме командира) имеют цвет(а) Color;
    // IncludeHand=true (умолч. — как было раньше, ради обратной совместимости с уже настроенными картами)
    // добавляет ещё и РУКУ (Проповедник: «если все карты жёлтого цвета — удвойте здоровье» — OnMatchStart
    // резолвится на ПЕРВОМ ХОДУ, стартовая рука уже раздана, скан только колоды её пропустил бы). Для карт
    // БЕЗ такой привязки к таймингу матча (кастуется в любой момент — «Отравленный кортик»: печатный текст
    // говорит только «в вашей колоде», про руку ни слова) ставь IncludeHand=false — иначе случайная не той
    // расцветки карта в руке роняла условие, хотя по тексту карты не должна была вообще участвовать (баг
    // 2026-08-24: «не накладывал яд при пустой колоде» — колода вправду vacuous true, а вот рука с чужим
    // цветом внутри — нет, и это никак не было видно игроку). Хоть одна карта БЕЗ нужного цвета (в т.ч.
    // бесцветная) → не готово. Мультицвет ок: карта должна СОДЕРЖАТЬ все флаги маски Color. Пересчёт на
    // событиях состава зон (замешанное оппонентом Вонючее облако ЛОМАЕТ условие — осмысленная контр-игра).
    // Теги зеркальны → IsReady одинаков на обоих клиентах.
    [Serializable]
    public sealed class AllCardsHaveColorCondition : ICondition
    {
        public EnumService.Element Color = EnumService.Element.Yellow;
        public bool IncludeHand = true;

        public bool IsReady { get; private set; }
        public event Action Changed;

        AbilityContext _ctx;

        public void Init(AbilityContext ctx)
        {
            _ctx = ctx;
            GameEventBus.Subscribe<CardDrawnEvent>(this, _ => Recompute());
            GameEventBus.Subscribe<CardGeneratedEvent>(this, _ => Recompute());
            GameEventBus.Subscribe<CardPlayedEvent>(this, _ => Recompute());
            GameEventBus.Subscribe<TurnStartedEvent>(this, _ => Recompute());
            Recompute();
        }

        void Recompute()
        {
            int ownerId = RuleUtil.OwnerId(_ctx.World, _ctx.CardEntity);
            bool now = ownerId >= 0 && AllColored<DeckTag>(ownerId) && (!IncludeHand || AllColored<HandTag>(ownerId));
            if (now == IsReady) return;
            IsReady = now;
            Changed?.Invoke();
        }

        bool AllColored<TZone>(int ownerId) where TZone : struct
        {
            var world = _ctx.World;
            var owner = world.GetPool<OwnerComponent>();
            var model = world.GetPool<CardModelComponent>();
            var commander = world.GetPool<CommanderTag>();
            foreach (var e in world.Filter<TZone>().Inc<CardModelComponent>().Inc<OwnerComponent>().End())
            {
                if (owner.Get(e).OwnerId != ownerId) continue;
                if (commander.Has(e)) continue;                          // командир — не из 20 карт колоды
                if ((model.Get(e).Element & Color) != Color) return false;   // карта без нужного цвета
            }
            return true;
        }

        public void Dispose() => GameEventBus.UnsubscribeAll(this);
    }

    // === class (OOP) === Готово, пока ВСЕ карты владельца в КОЛОДЕ принадлежат архетипу (Королёвский
    // сорняк: «если в вашей колоде одни сорняки»). Строго по тексту карты — только колода (в отличие от
    // AllCardsHaveColorCondition, которому руку навязывает тайминг OnMatchStart); IncludeHand=true добавляет
    // руку. Любая карта БЕЗ архетипа (в т.ч. спелл/чара — у них архетипов не бывает) ломает условие.
    // Пустая колода → готово (vacuous true: «заполнить сторону из пустой колоды» всё равно no-op).
    // Архетипы вешаются на ините сущности (CardModel.Init → ArchetypeTag.Apply) → видны и в колоде.
    // Пересчёт на событиях состава зон (как соседи) + страховочно на старте хода. Теги зеркальны →
    // IsReady одинаков на обоих клиентах.
    [Serializable]
    public sealed class AllCardsHaveArchetypeCondition : ICondition
    {
        [UnityEngine.SerializeReference] public Game.Core.Shared.Interface.ICreatureTag Archetype;
        [UnityEngine.Tooltip("Проверять и РУКУ тоже (как у цветового условия). По умолч. только колода — по тексту карты.")]
        public bool IncludeHand = false;

        public bool IsReady { get; private set; }
        public event Action Changed;

        AbilityContext _ctx;

        public void Init(AbilityContext ctx)
        {
            _ctx = ctx;
            GameEventBus.Subscribe<CardDrawnEvent>(this, _ => Recompute());
            GameEventBus.Subscribe<CardGeneratedEvent>(this, _ => Recompute());
            GameEventBus.Subscribe<CardPlayedEvent>(this, _ => Recompute());
            GameEventBus.Subscribe<CreatureInvokedEvent>(this, _ => Recompute());
            GameEventBus.Subscribe<TurnStartedEvent>(this, _ => Recompute());
            Recompute();
        }

        void Recompute()
        {
            int ownerId = RuleUtil.OwnerId(_ctx.World, _ctx.CardEntity);
            bool now = ownerId >= 0 && Archetype != null
                       && AllTagged<DeckTag>(ownerId)
                       && (!IncludeHand || AllTagged<HandTag>(ownerId));
            if (now == IsReady) return;
            IsReady = now;
            Changed?.Invoke();
        }

        bool AllTagged<TZone>(int ownerId) where TZone : struct
        {
            var world = _ctx.World;
            var owner = world.GetPool<OwnerComponent>();
            var commander = world.GetPool<CommanderTag>();
            var creature = world.GetPool<CreatureTag>();
            foreach (var e in world.Filter<TZone>().Inc<CardModelComponent>().Inc<OwnerComponent>().End())
            {
                if (owner.Get(e).OwnerId != ownerId) continue;
                if (commander.Has(e)) continue;                  // командир — не из 20 карт колоды
                // Архетип — трайб СУЩЕСТВ (ICreatureTag); спелл/чара не могут его иметь в принципе, поэтому
                // раньше ЛЮБОЙ спелл/чара в зоне валили всю проверку (Has всегда false для не-существа) —
                // «все карты архетипа X» на практике требовало колоду БЕЗ единого спелла. Пропускаем не-существ:
                // они не считаются ни за, ни против, проверяем только существ (без архетипа — не проходят).
                if (!creature.Has(e)) continue;
                if (!Archetype.Has(world, e)) return false;
            }
            return true;
        }

        public void Dispose() => GameEventBus.UnsubscribeAll(this);
    }

    // === class (OOP) === Готово РОВНО на каждое Every-е срабатывание каста карты владельца (Волшебник Упс:
    // «каждое 5-е заклинание» → Every=5, CardScope=Spell). Считает СВОИМ счётчиком (не делит состояние ни
    // с каким триггером) — переиспользуемо с ЛЮБЫМ триггером/эффектом, который умеет ConditionRoot
    // (EffectBase). Готовность НЕ «залипает»: она = «счётчик сейчас кратен Every» — как только приходит
    // следующий подходящий каст (счётчик сдвигается на 1), готовность сама уходит до следующего кратного.
    // Это и заменяет явный «сброс»: эффект-потребитель (SetTriggerMultiplierEffect{Charges=1}) успевает
    // среагировать на резолве СРАЗУ после Every-го каста — до следующего подходящего каста готовность
    // не гаснет, а гонки нет: счётчик и IsReady меняются синхронно, в момент CardCastEvent, тем же тиком,
    // что и Mark() у триггера, который эту способность зовёт.
    [Serializable]
    public sealed class EveryNthCardCastCondition : ICondition
    {
        [UnityEngine.Tooltip("Каждое N-е подходящее срабатывание — готово.")]
        public int Every = 5;
        [UnityEngine.Tooltip("Считать только карты этого типа владельца (Any = любые).")]
        public MultiplierCardScope CardScope = MultiplierCardScope.Any;

        public bool IsReady { get; private set; }
        public event Action Changed;

        AbilityContext _ctx;
        int _count;

        public void Init(AbilityContext ctx)
        {
            _ctx = ctx;
            GameEventBus.Subscribe<Game.Core.Events.CardCastEvent>(this, OnCast);
        }

        void OnCast(Game.Core.Events.CardCastEvent e)
        {
            if (Every <= 0) return;
            var owner = _ctx.World.GetPool<OwnerComponent>();
            if (!owner.Has(e.CardEntity) || !owner.Has(_ctx.CardEntity)) return;
            if (owner.Get(e.CardEntity).OwnerId != owner.Get(_ctx.CardEntity).OwnerId) return;   // только СВОИ карты владельца

            // Карта разыграна КАК СЛЕДСТВИЕ другого эффекта (Развилка и подобные дискавер-с-автоигрой) —
            // не самостоятельный ход игрока, в «N-е заклинание» считаться не должна. См. CauseStamp.IsCaused.
            if (CauseStamp.IsCaused(_ctx.World, e.CardEntity)) return;

            if (CardScope != MultiplierCardScope.Any)
            {
                var model = _ctx.World.GetPool<CardModelComponent>();
                EnumService.CardType wanted = CardScope switch
                {
                    MultiplierCardScope.Creature => EnumService.CardType.Creature,
                    MultiplierCardScope.Spell    => EnumService.CardType.Spell,
                    MultiplierCardScope.Charm    => EnumService.CardType.Charm,
                    _ => EnumService.CardType.Creature,
                };
                if (!model.Has(e.CardEntity) || model.Get(e.CardEntity).CardType != wanted) return;
            }

            _count++;
            bool now = _count % Every == 0;
            if (now == IsReady) return;
            IsReady = now;
            Changed?.Invoke();
        }

        public void Dispose() => GameEventBus.UnsubscribeAll(this);
    }

    // === class (OOP) === Готово, пока владелец КОНТРОЛИРУЕТ хотя бы одно существо архетипа НА ПОЛЕ (Без
    // имени: «если вы контролируете Сорняк/Чёрта»). В отличие от AllCardsHaveArchetypeCondition (там —
    // ВСЯ колода целиком) это про БОРД и «хотя бы один», без вырожденного true на пустой зоне: пустой борд
    // = НЕ готово (не «сорняков нет, значит условие выполнено», а прямо противоположное по смыслу карты).
    // Пересчёт на составе борда (выход/смерть существа) + страховочно на старте хода.
    [Serializable]
    public sealed class ControlsArchetypeOnBoardCondition : ICondition
    {
        [UnityEngine.SerializeReference] public Game.Core.Shared.Interface.ICreatureTag Archetype;

        public bool IsReady { get; private set; }
        public event Action Changed;

        AbilityContext _ctx;

        public void Init(AbilityContext ctx)
        {
            _ctx = ctx;
            GameEventBus.Subscribe<CreatureInvokedEvent>(this, _ => Recompute());
            GameEventBus.Subscribe<CreatureDiedEvent>(this, _ => Recompute());
            GameEventBus.Subscribe<TurnStartedEvent>(this, _ => Recompute());
            Recompute();
        }

        void Recompute()
        {
            int ownerId = RuleUtil.OwnerId(_ctx.World, _ctx.CardEntity);
            bool now = ownerId >= 0 && Archetype != null && AnyOnBoard(ownerId);
            // ВРЕМЕННО (баг: карта с ControlsArchetypeOnBoardCondition не подсвечивается рыжим, хотя
            // архетип на поле есть) — видим ownerId/результат скана/итоговое IsReady на каждый пересчёт.
            UnityEngine.Debug.Log($"[CondArchetype] card={_ctx.CardEntity} archetype={Archetype?.Key} ownerId={ownerId} now={now} wasReady={IsReady}");
            if (now == IsReady) return;
            IsReady = now;
            Changed?.Invoke();
        }

        bool AnyOnBoard(int ownerId)
        {
            var world = _ctx.World;
            var owner = world.GetPool<OwnerComponent>();
            foreach (var e in world.Filter<BoardTag>().Inc<CreatureTag>().Exc<DeadTag>().End())
            {
                if (!owner.Has(e) || owner.Get(e).OwnerId != ownerId) continue;
                if (Archetype.Has(world, e)) return true;
            }
            return false;
        }

        public void Dispose() => GameEventBus.UnsubscribeAll(this);
    }

    /// <summary>Что считаем (относительно владельца способности). OwnPlayerOwnTurn — урон своему игроку «на
    /// своём ходу» (от себя, Source==Target). Прочее — суммарный урон игроку/существам своей/вражеской стороны.</summary>
    public enum DamageScope { OwnPlayerOwnTurn, OwnPlayer, OwnCreatures, EnemyPlayer, EnemyCreatures }

    // === class (OOP) === Готово, когда накоплено >= Threshold урона выбранной категории за матч (Вуду-будду:
    // OwnPlayerOwnTurn). Реактивно (пересчёт на DamageTrackedEvent). Данные — MatchCounterComponent (копит
    // MatchCounterTrackerSystem; зеркально → IsReady одинаков на обоих).
    [Serializable]
    public sealed class TakeDamageCondition : ICondition
    {
        public DamageScope Scope = DamageScope.OwnPlayerOwnTurn;
        public int Threshold = 1;

        public bool IsReady { get; private set; }
        public event Action Changed;

        AbilityContext _ctx;

        public void Init(AbilityContext ctx)
        {
            _ctx = ctx;
            GameEventBus.Subscribe<DamageTrackedEvent>(this, OnDamage);
            Recompute();
        }

        void OnDamage(DamageTrackedEvent e) => Recompute();

        void Recompute()
        {
            bool now = Accumulated() >= Threshold;
            if (now == IsReady) return;
            IsReady = now;
            Changed?.Invoke();
        }

        int Accumulated()
        {
            bool own = Scope == DamageScope.OwnPlayerOwnTurn || Scope == DamageScope.OwnPlayer || Scope == DamageScope.OwnCreatures;
            int pe = own ? _ctx.PlayerEntity : Opponent();
            var pool = _ctx.World.GetPool<MatchCounterComponent>();
            if (pe < 0 || !pool.Has(pe)) return 0;
            ref var c = ref pool.Get(pe);
            switch (Scope)
            {
                case DamageScope.OwnPlayerOwnTurn:               return c.PlayerDamageTakenOwnTurn;
                case DamageScope.OwnPlayer: case DamageScope.EnemyPlayer:    return c.PlayerDamageTaken;
                case DamageScope.OwnCreatures: case DamageScope.EnemyCreatures: return c.CreaturesDamageTaken;
                default: return 0;
            }
        }

        int Opponent()
        {
            var pp = _ctx.World.GetPool<PlayerComponent>();
            if (!pp.Has(_ctx.PlayerEntity)) return -1;
            int myId = pp.Get(_ctx.PlayerEntity).PlayerId;
            foreach (var e in _ctx.World.Filter<PlayerComponent>().End())
                if (pp.Get(e).PlayerId != myId) return e;
            return -1;
        }

        public void Dispose() => GameEventBus.UnsubscribeAll(this);
    }

    // === class (OOP) === Готово, пока HP игрока-владельца ниже порога.
    // Реагирует на PlayerDamagedEvent (как ReceivedDamageCondition, но абсолютный
    // порог по текущему HP, а не накопленный урон).
    [Serializable]
    public sealed class OwnerHealthBelowCondition : ICondition
    {
        public int Threshold = 10;

        public bool IsReady { get; private set; }
        public event Action Changed;

        AbilityContext _ctx;

        public void Init(AbilityContext ctx)
        {
            _ctx = ctx;
            GameEventBus.Subscribe<PlayerDamagedEvent>(this, OnDamaged);
            Recompute();
        }

        void OnDamaged(PlayerDamagedEvent e)
        {
            if (e.PlayerEntity != _ctx.PlayerEntity) return;
            Recompute();
        }

        void Recompute()
        {
            var hp = _ctx.World.GetPool<HealthComponent>();
            bool now = hp.Has(_ctx.PlayerEntity) && hp.Get(_ctx.PlayerEntity).Current < Threshold;
            if (now == IsReady) return;
            IsReady = now;
            Changed?.Invoke();
        }

        public void Dispose() => GameEventBus.UnsubscribeAll(this);
    }

    // === class (OOP) === Готово, пока HP игрока-владельца ниже порога.
    // Реагирует на PlayerDamagedEvent (как ReceivedDamageCondition, но абсолютный
    // порог по текущему HP, а не накопленный урон).
    [Serializable]
    public sealed class ResourceAvailableCondition : ICondition
    {
        public int Value = 0;
        public EnumService.ResourceType ResourceType;

        public bool IsReady { get; private set; }
        public event Action Changed;

        AbilityContext _ctx;

        public void Init(AbilityContext ctx)
        {
            _ctx = ctx;
            GameEventBus.Subscribe<ResourceChangedEvent>(this, OnResourceChanged);
            Recompute();
        }

        void OnResourceChanged(ResourceChangedEvent e)
        {
            bool result = false;

            if (e.Type != ResourceType) return;

            switch (e.Type)
            {
                case EnumService.ResourceType.Gold:
                    ref var goldComp = ref _ctx.World.GetPool<GoldComponent>().Get(_ctx.PlayerEntity);

                    result = goldComp.Current >= Value; 
                    break;
                case EnumService.ResourceType.Mana:
                    ref var manaComp = ref _ctx.World.GetPool<ManaComponent>().Get(_ctx.PlayerEntity);

                    result = manaComp.Current >= Value;
                    break; 
            }
             
            if (result == IsReady) return;
            IsReady = result;
            Changed?.Invoke();
        }

        void Recompute()
        {
            bool result = false;

            switch (ResourceType)
            {
                case EnumService.ResourceType.Gold:
                    ref var goldComp = ref _ctx.World.GetPool<GoldComponent>().Get(_ctx.PlayerEntity);
                    result = goldComp.Current >= Value;
                    break;
                case EnumService.ResourceType.Mana:
                    ref var manaComp = ref _ctx.World.GetPool<ManaComponent>().Get(_ctx.PlayerEntity);
                    result = manaComp.Current >= Value;
                    break; 
            }

            if (result == IsReady) return;
            IsReady = result;
            Changed?.Invoke();
        }

        public void Dispose() => GameEventBus.UnsubscribeAll(this);
    }
}
