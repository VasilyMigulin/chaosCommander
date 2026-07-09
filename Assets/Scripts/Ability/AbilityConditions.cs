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

    // === class (OOP) === Готово, пока ВСЕ карты владельца в КОЛОДЕ И РУКЕ (кроме командира) имеют цвет(а)
    // Color (Проповедник: «если все карты жёлтого цвета — удвойте здоровье»). Хоть одна карта БЕЗ нужного
    // цвета (в т.ч. бесцветная) → не готово. Обе зоны обязательны: OnMatchStart резолвится на ПЕРВОМ ХОДУ,
    // стартовая рука уже раздана — скан только колоды её пропустил бы (старый баг RequireDeckColor: к тому же
    // проверял «есть хотя бы одна цветная», а не «все»). Мультицвет ок: карта должна СОДЕРЖАТЬ все флаги
    // маски Color. Пересчёт на событиях состава зон (замешанное оппонентом Вонючее облако ЛОМАЕТ условие —
    // осмысленная контр-игра). Теги зеркальны → IsReady одинаков на обоих клиентах.
    [Serializable]
    public sealed class AllCardsHaveColorCondition : ICondition
    {
        public EnumService.Element Color = EnumService.Element.Yellow;

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
            bool now = ownerId >= 0 && AllColored<DeckTag>(ownerId) && AllColored<HandTag>(ownerId);
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
