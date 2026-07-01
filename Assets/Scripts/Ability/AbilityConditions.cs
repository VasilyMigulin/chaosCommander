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
