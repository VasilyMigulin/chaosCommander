using System;
using Game.Core.Events;
using Game.Core.Ecs.Components;
using Game.Core.Service;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;
using UnityEngine;

namespace Game.Core.Ability
{
    // ─────────────────────────────────────────────────────────────────────────
    // СЭМПЛЫ — показывают форму реализации. Каждый поведенческий объект подписывается
    // на шину с owner = this, а в Dispose делает GameEventBus.UnsubscribeAll(this).
    // При срабатывании триггер ВЕШАЕТ AbilityCastEvent на свою ability-сущность —
    // дальше RunCheckAbilityRulesSystem проверит правила и поставит в очередь.
    // ─────────────────────────────────────────────────────────────────────────

    // === helper === общая «отметка срабатывания» триггера: вешает AbilityCastEvent на ability-сущность.
    internal static class AbilityFire
    {
        // triggerKey (опц.) — тип триггера для множителя частоты (CastMultiplierService): "OnTurnStart" и т.п.
        // Если множитель > 1 (Временная петля), ставим PendingCastsComponent → способность разрешится N раз.
        // subjectEntity (опц.) — ВИНОВНИК срабатывания (OnCreatureInvoked → вышедшее существо): едет в
        // TriggerSubjectComponent для TargetSelection.TriggerSubject («Неудачная молитва»).
        // isReaction — активация есть РЕАКЦИЯ на чужой резолв (ITrigger.IsReaction): встанет в очередь
        // сразу за своей причиной, а не в конец (см. ActivationKey).
        public static void Mark(EcsWorld world, int abilityEntity, int cardEntity, int playerEntity, string triggerKey = null, int subjectEntity = -1, bool isReaction = false)
        {
            // ВРЕМЕННО: видим, что триггер сработал и не загейчен ли пассивом.
            UnityEngine.Debug.Log($"[Mark] card={cardEntity} ability={abilityEntity} trigger={triggerKey ?? "?"} active={TurnGate.IsLocalActive(world)}");
            // Пассив не запускает триггеры — ability-флоу у него драйвит реплей снапшотов.
            if (!TurnGate.IsLocalActive(world)) return;

            // Гейт уровня (карта-с-уровнями): способность уровня активна только когда карта на этом уровне.
            // Уровень зеркален (источник золото зеркален) → гейт одинаков у обоих клиентов, спец-синка не нужно.
            var gatePool = world.GetPool<AbilityTierGateComponent>();
            if (gatePool.Has(abilityEntity))
            {
                var tierPool = world.GetPool<CardTierComponent>();
                int cur = tierPool.Has(cardEntity) ? tierPool.Get(cardEntity).CurrentTier : 0;
                if (gatePool.Get(abilityEntity).Tier != cur) return;   // не наш уровень — молчим
            }

            // ОБЩЕЕ ПРАВИЛО (юзер 2026-07-30): пока на карте КУЛДАУН (командир после гибели ждёт хода
            // доступности), её способности НЕ РАБОТАЮТ — ни триггерные, ни ауры (см. PassiveAuraSystem /
            // BuffPerCharmSystem). Иначе командир-аура «Шальной десница» продолжал бы светить из руки,
            // будучи фактически выведенным из игры на ход.
            if (world.GetPool<CommanderCooldownComponent>().Has(cardEntity)) return;

            // Лимит активаций за ход (Ability.MaxActivationsPerTurn; 0 = безлимит → компонента нет).
            // Считаются ФАКТИЧЕСКИЕ применения (инкремент в RunResolveAbilityQueueSystem), поэтому
            // холостые срабатывания (фильтры не дали целей) лимит не тратят. Сброс — старт хода владельца.
            var limitPool = world.GetPool<AbilityUseLimitComponent>();
            if (limitPool.Has(abilityEntity))
            {
                ref var limit = ref limitPool.Get(abilityEntity);
                if (limit.Max > 0 && limit.UsedThisTurn >= limit.Max) return;   // исчерпана до конца хода
            }

            var pool = world.GetPool<AbilityCastEvent>();
            if (pool.Has(abilityEntity)) return;
            ref var c = ref pool.Add(abilityEntity);
            c.CardEntity = cardEntity;
            c.OwnerPlayerEntity = playerEntity;

            // Ключ триггера ЭТОЙ активации (для AbilityResolveContext.TriggerKey — см. AbilityTriggerKeyComponent).
            var keyPool = world.GetPool<AbilityTriggerKeyComponent>();
            if (!keyPool.Has(abilityEntity)) keyPool.Add(abilityEntity);
            keyPool.Get(abilityEntity).Key = triggerKey;
            keyPool.Get(abilityEntity).IsReaction = isReaction;   // порядок в очереди: реакция идёт за причиной

            // Виновник: ставим/перезаписываем; без виновника — снимаем (не протухает от прошлого файра).
            var subjPool = world.GetPool<TriggerSubjectComponent>();
            if (subjectEntity >= 0)
            {
                if (!subjPool.Has(abilityEntity)) subjPool.Add(abilityEntity);
                subjPool.Get(abilityEntity).Entity = subjectEntity;
            }
            else if (subjPool.Has(abilityEntity)) subjPool.Del(abilityEntity);

            if (triggerKey != null)
            {
                var ownerPool = world.GetPool<OwnerComponent>();
                int ownerId = ownerPool.Has(cardEntity) ? ownerPool.Get(cardEntity).OwnerId : -1;
                // Множитель ключуется комбинацией «ТипКарты/Триггер» (Spell/OnCast и т.п.) + wildcard "*/Триггер".
                var modelPool = world.GetPool<CardModelComponent>();
                var cardType = modelPool.Has(cardEntity) ? modelPool.Get(cardEntity).CardType : EnumService.CardType.Creature;
                int casts = CastMultiplierService.Casts(ownerId, cardType, triggerKey);
                if (casts > 1)
                {
                    // Заряд съедаемых источников тратится ЗДЕСЬ — один раз на СРАБАТЫВАНИЕ (не на каждый из
                    // N внутренних резолв-повторов ниже, иначе съедаемый(2) сгорал бы за один же тройной каст).
                    CastMultiplierService.ConsumeCharge(ownerId, cardType, triggerKey);
                    var cp = world.GetPool<PendingCastsComponent>();
                    if (!cp.Has(abilityEntity)) cp.Add(abilityEntity);
                    cp.Get(abilityEntity).Remaining = casts;
                }
            }
        }
    }

    // === class (OOP) === PUSH-триггер: сам подписан, при срабатывании вешает AbilityCastEvent.
    [Serializable]
    public sealed class OnDieTrigger : ITrigger
    {
        EcsWorld _world;
        int _abilityEntity, _cardEntity, _playerEntity;

        public bool AiDeathTrigger => true;   // хрип: при полезном эффекте ИИ нарывается на смерть (см. ITrigger)
        public bool IsReaction    => true;    // хрип встаёт сразу за добившей способностью (см. ActivationKey)

        public void Init(EcsWorld world, int abilityEntity, int cardEntity, int playerEntity)
        {
            _world = world; _abilityEntity = abilityEntity; _cardEntity = cardEntity; _playerEntity = playerEntity;
            GameEventBus.Subscribe<CreatureDiedEvent>(this, OnDied);
        }

        void OnDied(CreatureDiedEvent e)
        {
            if (e.CardEntity != _cardEntity) return;                     // фильтр: это моя карта?

            // Задачи: «сработал хрип». Публикуем ДО активного гейта Mark — чтобы засчитывалось и в ход
            // противника (CreatureDiedEvent приходит на обоих клиентах; трекер фильтрует по своему LocalPlayerId).
            var ownerPool = _world.GetPool<OwnerComponent>();
            GameEventBus.Publish(new DeathrattleTrackedEvent
            {
                OwnerId = ownerPool.Has(_cardEntity) ? ownerPool.Get(_cardEntity).OwnerId : -1
            });

            AbilityFire.Mark(_world, _abilityEntity, _cardEntity, _playerEntity, TriggerKeys.OnDie,
                             isReaction: IsReaction); // множитель + порядок в очереди
        }

        public void Dispose() => GameEventBus.UnsubscribeAll(this);
    }

    // === class (OOP) === Триггер каста — единый канал активации (через шину, не через ECS-тег).
    [Serializable]
    public sealed class OnCastTrigger : ITrigger
    {
        public bool FiresOnCast => true;

        EcsWorld _world;
        int _abilityEntity, _cardEntity, _playerEntity;

        public void Init(EcsWorld world, int abilityEntity, int cardEntity, int playerEntity)
        {
            _world = world; _abilityEntity = abilityEntity; _cardEntity = cardEntity; _playerEntity = playerEntity;
            GameEventBus.Subscribe<CardCastEvent>(this, OnCast);
        }

        void OnCast(CardCastEvent e)
        {
            if (e.CardEntity != _cardEntity) return;
            AbilityFire.Mark(_world, _abilityEntity, _cardEntity, _playerEntity, TriggerKeys.OnCast); // множитель
        }

        public void Dispose() => GameEventBus.UnsubscribeAll(this);
    }

    // === class (OOP) === PUSH-триггер «При сбросе»: карту сбросили из руки в кладбище (DiscardEffect →
    // CardDiscardedEvent). Тот же паттерн, что OnDieTrigger, но по событию сброса. «Выгребной мусор»:
    // при сбросе выставляет себя+копию на поле (FillRowWithCopyOfSelf{2}). Гейт актив/пассив — в
    // AbilityFire.Mark (пассив реплеит снапшот резолва).
    [Serializable]
    public sealed class OnDiscardTrigger : ITrigger
    {
        EcsWorld _world;
        int _abilityEntity, _cardEntity, _playerEntity;

        public void Init(EcsWorld world, int abilityEntity, int cardEntity, int playerEntity)
        {
            _world = world; _abilityEntity = abilityEntity; _cardEntity = cardEntity; _playerEntity = playerEntity;
            GameEventBus.Subscribe<CardDiscardedEvent>(this, OnDiscarded);
        }

        void OnDiscarded(CardDiscardedEvent e)
        {
            if (e.CardEntity != _cardEntity) return;
            AbilityFire.Mark(_world, _abilityEntity, _cardEntity, _playerEntity, TriggerKeys.OnDiscard);
        }

        public void Dispose() => GameEventBus.UnsubscribeAll(this);
    }

    // === class (OOP) === Параметризованное event-driven условие: порог — это поле.
    [Serializable]
    public sealed class ReceivedDamageCondition : ICondition
    {
        public int Threshold = 10;

        public bool IsReady { get; private set; }
        public event Action Changed;

        AbilityContext _ctx;
        int _accumulated;   // упрощённо держим в самом условии; в бою лучше читать из struct-счётчика на игроке

        public void Init(AbilityContext ctx)
        {
            _ctx = ctx;
            GameEventBus.Subscribe<PlayerDamagedEvent>(this, OnDamaged);
            Recompute();
        }

        void OnDamaged(PlayerDamagedEvent e)
        {
            if (e.PlayerEntity != _ctx.PlayerEntity) return;
            _accumulated += e.Amount;
            Recompute();
        }

        void Recompute()
        {
            bool now = _accumulated >= Threshold;
            if (now == IsReady) return;
            IsReady = now;
            Changed?.Invoke();
        }

        public void Dispose() => GameEventBus.UnsubscribeAll(this);
    }

    // === class (OOP) === Сэмпл-правило: читает world (здесь — заглушка).
    [Serializable]
    public sealed class EnemyCreatureOnBoardRule : IRule
    {
        public bool Evaluate(EcsWorld world, int cardEntity, int playerEntity)
        {
            // TODO: квери world — есть ли существо под контролем оппонента на борде.
            return true;
        }
    }

    // === class (OOP) === Лог-эффект для проверки пайплайна: пишет в консоль при применении.
    [Serializable]
    public sealed class LogEffect : IEffect
    {
        public string Message = "effect applied";
        [SerializeReference] public Condition ConditionRoot;

        public void Init(EcsWorld world, int cardEntity, int playerEntity)
            => ConditionRoot?.Init(new AbilityContext { World = world, CardEntity = cardEntity, PlayerEntity = playerEntity });
        public void Dispose() => ConditionRoot?.Dispose();
        public bool IsReady => ConditionRoot == null || ConditionRoot.IsReady;

        public void Apply(EcsWorld world, int cardEntity, int target)
            => UnityEngine.Debug.Log($"[Ability] LogEffect: {Message} (target={target}, card={cardEntity})");
    }

    // === class (OOP) === Сэмпл-эффект: держит своё составное условие Condition (null = всегда готов).
    [Serializable]
    public sealed class DealDamageEffect : IEffect, IDynamicValue
    {
        public Game.Core.Shared.Interface.AiEffectRole AiRole => Game.Core.Shared.Interface.AiEffectRole.Damage;
        public int Amount = 1;
        [SerializeReference] public Condition ConditionRoot;

        public void Init(EcsWorld world, int cardEntity, int playerEntity)
            => ConditionRoot?.Init(new AbilityContext { World = world, CardEntity = cardEntity, PlayerEntity = playerEntity });
        public void Dispose() => ConditionRoot?.Dispose();
        public bool IsReady => ConditionRoot == null || ConditionRoot.IsReady;

        // Динамическое значение для описания (*N* → урон). Пока = Amount; когда появятся
        // глобальные модификаторы урона — читать их здесь тем же путём, что и Apply.
        public int DynamicValueCount => 1;
        public int GetDynamicValue(int index, EcsWorld world, int cardEntity, int playerEntity) => Amount;

        public void Apply(EcsWorld world, int cardEntity, int target)
        {
            // Урон наносим через TakeDamageEvent — его обрабатывает TakeDamageSystem
            // (события урона/смерти, DeadTag). Attacker = карта-источник (у неё есть OwnerComponent).
            if (!world.GetPool<HealthComponent>().Has(target)) return;   // цель без здоровья

            var dmgPool = world.GetPool<TakeDamageEvent>();
            if (!dmgPool.Has(target)) dmgPool.Add(target);
            ref var d = ref dmgPool.Get(target);
            d.Amount += Amount;            // накапливаем, если несколько эффектов бьют в одну цель
            d.Attacker = cardEntity;
        }
    }

    // === class (OOP) === NonTarget-эффект: +мана игроку (target = сущность игрока-владельца).
    [Serializable]
    public sealed class GainManaEffect : ICasterScopedEffect, IDynamicValue
    {
        public Game.Core.Shared.Interface.AiEffectRole AiRole => Game.Core.Shared.Interface.AiEffectRole.Resource;
        public int Amount = 1;
        [SerializeReference] public Condition ConditionRoot;

        public void Init(EcsWorld world, int cardEntity, int playerEntity)
            => ConditionRoot?.Init(new AbilityContext { World = world, CardEntity = cardEntity, PlayerEntity = playerEntity });
        public void Dispose() => ConditionRoot?.Dispose();
        public bool IsReady => ConditionRoot == null || ConditionRoot.IsReady;

        public int DynamicValueCount => 1;
        public int GetDynamicValue(int index, EcsWorld world, int cardEntity, int playerEntity) => Amount;

        public void Apply(EcsWorld world, int cardEntity, int target)
        {
            var pool = world.GetPool<ManaComponent>();
            if (!pool.Has(target)) return;   // target = игрок (NonTarget)
            ref var mana = ref pool.Get(target);
            // Мана копится ТОЛЬКО эффектами (стартует 0/0, доход хода её не даёт), поэтому «дать ману» поднимает
            // и Max, и Current (иначе Min(Current+Amount, Max=0)=0 → эффект не работал). Кап 10 (как у золота).
            const int ManaCap = 10;
            mana.Max     = Math.Min(mana.Max + Amount, ManaCap);
            mana.Current = Math.Min(mana.Current + Amount, mana.Max);

            var playerPool = world.GetPool<PlayerComponent>();
            GameEventBus.Publish(new ResourceChangedEvent
            {
                isLocalPlayer = playerPool.Has(target) && playerPool.Get(target).IsLocalPlayer,
                Type     = EnumService.ResourceType.Mana,
                NewValue = mana.Current,
                MaxValue = mana.Max,
            });
        }
    }
}
