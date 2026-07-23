using System.Collections.Generic;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Инкрементит MatchCounterComponent игрока по событиям матча:
    ///   CardPlayedEvent     → CountsByModelId    (разыграно; PlayerId = id игрока);
    ///   CardDrawnEvent      → DrawnByModelId     (взято; PlayerId = СУЩНОСТЬ игрока);
    ///   CardGeneratedEvent  → GeneratedByModelId (создано генератором; GeneratorPlayerId = id игрока);
    ///   CreatureInvokedEvent→ InvokedByArchetype (призвано; владелец по OwnerComponent; ключ по тегам).
    /// События идут на обоих клиентах (в т.ч. при реплее) → счётчики зеркальны, синк не нужен.
    /// </summary>
    public sealed class MatchCounterTrackerSystem : IEcsInitSystem, IEcsDestroySystem, System.IDisposable
    {
        readonly EcsWorldInject _world = default;
        readonly EcsPoolInject<MatchCounterComponent> _counterPool = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsPoolInject<CardModelComponent> _modelPool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;
        readonly EcsPoolInject<ArchetypeComponent> _archPool = default;
        readonly EcsPoolInject<TokenTag> _tokenPool = default;
        readonly EcsFilterInject<Inc<PlayerComponent>> _playerFilter = default;

        bool _subscribed;

        // Чей сейчас ход — по TurnStartedEvent (зеркально на обоих клиентах, реплей его тоже публикует).
        // НЕ по ActiveState: тот вешается только после оседания стартового каскада (RunActivateSystem) и
        // снимается в начале конечного — урон от чар/триггеров в начале и конце хода остался бы «вне хода»,
        // хотя по тексту карт («во время своего хода», Вуду-будду) это ещё ход игрока.
        int _currentTurnPlayerId = -1;

        public void Init(IEcsSystems systems) => Subscribe();

        // EcsSystems.Destroy() ищет IEcsDestroySystem, не System.IDisposable — без этого моста Dispose()
        // фреймворк никогда не вызывал бы (см. EcsRunHandler/TutorialEcsHandler.Dispose → _allSystems.Destroy()).
        public void Destroy(IEcsSystems systems) => Dispose();

        public void Dispose()
        {
            if (!_subscribed) return;
            GameEventBus.Unsubscribe<CardPlayedEvent>(OnPlayed);
            GameEventBus.Unsubscribe<CardDrawnEvent>(OnDrawn);
            GameEventBus.Unsubscribe<CardGeneratedEvent>(OnGenerated);
            GameEventBus.Unsubscribe<CreatureInvokedEvent>(OnInvoked);
            GameEventBus.Unsubscribe<DamageTrackedEvent>(OnDamage);
            GameEventBus.Unsubscribe<TurnStartedEvent>(OnTurnStarted);
            _subscribed = false;
        }
        void Subscribe()
        {
            if (_subscribed) return;
            _subscribed = true;
            GameEventBus.Subscribe<CardPlayedEvent>(OnPlayed);
            GameEventBus.Subscribe<CardDrawnEvent>(OnDrawn);
            GameEventBus.Subscribe<CardGeneratedEvent>(OnGenerated);
            GameEventBus.Subscribe<CreatureInvokedEvent>(OnInvoked);
            GameEventBus.Subscribe<DamageTrackedEvent>(OnDamage);
            GameEventBus.Subscribe<TurnStartedEvent>(OnTurnStarted);
        }

        // СИНХРОННО (Publish копирует список → безопасно): счётчик должен быть актуален К МОМЕНТУ резолва
        // способности в ТОМ ЖЕ кадре. Очередь+Run давала кадровую задержку → резолв читал нулевой счётчик
        // (Позвать рой n=0, Грыз/Гнидальф с self-count тоже). Теперь инкремент происходит сразу на событии.
        void OnPlayed(CardPlayedEvent e)      => TrackPlayed(e);
        void OnDrawn(CardDrawnEvent e)        => TrackDrawn(e);
        void OnGenerated(CardGeneratedEvent e)=> TrackGenerated(e);
        void OnInvoked(CreatureInvokedEvent e)=> TrackInvoked(e);
        void OnDamage(DamageTrackedEvent e)   => TrackDamage(e);
        void OnTurnStarted(TurnStartedEvent e)=> _currentTurnPlayerId = e.ActivePlayerId;

        void TrackDamage(DamageTrackedEvent e)
        {
            if (e.Amount <= 0 || e.TargetEntity < 0) return;
            if (_playerPool.Value.Has(e.TargetEntity))           // урон ПЛЕЕРУ
            {
                ref var c = ref Ensure(e.TargetEntity);
                c.PlayerDamageTaken += e.Amount;
                // Вуду-будду: «нанесли СЕБЕ урон во время СВОЕГО хода» — обе части буквально:
                // источник принадлежит цели И сейчас ход цели (окно TurnStartedEvent(я)→TurnStartedEvent(след.),
                // т.е. каскады начала/конца хода включены).
                if (e.SourcePlayerId == e.TargetPlayerId && e.TargetPlayerId == _currentTurnPlayerId)
                    c.PlayerDamageTakenOwnTurn += e.Amount;
            }
            else                                                  // урон СУЩЕСТВУ → копим владельцу
            {
                int pe = FindPlayerEntity(e.TargetPlayerId);
                if (pe < 0) return;
                ref var c = ref Ensure(pe);
                c.CreaturesDamageTaken += e.Amount;
            }
        }

        void TrackPlayed(CardPlayedEvent e)
        {
            if (e.CardEntity < 0 || !_modelPool.Value.Has(e.CardEntity)) return;
            int pe = FindPlayerEntity(e.PlayerId);
            if (pe < 0) return;
            ref var c = ref Ensure(pe);
            ref var model = ref _modelPool.Value.Get(e.CardEntity);
            Inc(c.CountsByModelId, model.ModelId);
            if (model.CardType == Game.Core.Service.EnumService.CardType.Spell)
            {
                c.SpellsPlayed++;   // «за каждый спелл в матче» (Моментум); событие на обоих → зеркально
                (c.SpellsPlayedLog ??= new System.Collections.Generic.List<SpellPlayRecord>())
                    .Add(new SpellPlayRecord { ExpansionId = model.ExpansionId, ModelId = model.ModelId });
            }
            else if (model.CardType == Game.Core.Service.EnumService.CardType.Charm
                     && !_tokenPool.Value.Has(e.CardEntity))   // токены не реплеим (Мистер Постоянство)
            {
                (c.CharmsPlayedLog ??= new System.Collections.Generic.List<SpellPlayRecord>())
                    .Add(new SpellPlayRecord { ExpansionId = model.ExpansionId, ModelId = model.ModelId });
            }
        }

        void TrackDrawn(CardDrawnEvent e)
        {
            if (e.CardEntity < 0 || !_modelPool.Value.Has(e.CardEntity)) return;
            int pe = e.PlayerId;   // у CardDrawnEvent PlayerId = СУЩНОСТЬ игрока
            if (!_playerPool.Value.Has(pe)) return;
            ref var c = ref Ensure(pe);
            Inc(c.DrawnByModelId, _modelPool.Value.Get(e.CardEntity).ModelId);
        }

        void TrackGenerated(CardGeneratedEvent e)
        {
            int pe = FindPlayerEntity(e.GeneratorPlayerId);
            if (pe < 0) return;
            ref var c = ref Ensure(pe);
            Inc(c.GeneratedByModelId, e.ModelId);
        }

        void TrackInvoked(CreatureInvokedEvent invokedEvent)
        {
            if (invokedEvent.CardEntity < 0 || !_ownerPool.Value.Has(invokedEvent.CardEntity)) return;
            int pe = FindPlayerEntity(_ownerPool.Value.Get(invokedEvent.CardEntity).OwnerId);
            if (pe < 0) return;
            ref var c = ref Ensure(pe);
            // ключи архетипов берём прямо из ArchetypeComponent (общий список) — без switch/новых тегов
            if (_archPool.Value.Has(invokedEvent.CardEntity))
            {
                var keys = _archPool.Value.Get(invokedEvent.CardEntity).Keys;
                if (keys != null) foreach (var k in keys) IncStr(c.InvokedByArchetype, k);
            }
        }

        ref MatchCounterComponent Ensure(int playerEntity)
        {
            if (!_counterPool.Value.Has(playerEntity)) _counterPool.Value.Add(playerEntity);
            ref var c = ref _counterPool.Value.Get(playerEntity);
            c.CountsByModelId    ??= new Dictionary<int, int>();
            c.DrawnByModelId     ??= new Dictionary<int, int>();
            c.GeneratedByModelId ??= new Dictionary<int, int>();
            c.InvokedByArchetype ??= new Dictionary<string, int>();
            return ref c;
        }

        static void Inc(Dictionary<int, int> d, int key)    { d.TryGetValue(key, out int v); d[key] = v + 1; }
        static void IncStr(Dictionary<string, int> d, string key) { d.TryGetValue(key, out int v); d[key] = v + 1; }

        int FindPlayerEntity(int playerId)
        {
            foreach (var pe in _playerFilter.Value)
                if (_playerPool.Value.Get(pe).PlayerId == playerId) return pe;
            return -1;
        }
    }
}
