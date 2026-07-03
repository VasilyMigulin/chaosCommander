using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Shared.Interface;
using Game.Core.Ecs.Systems;
using System.Collections.Generic;
using Leopotam.EcsLite.ExtendedSystems;
using Game.Core.Ecs.Components;
using NUnit.Framework.Constraints;

namespace Game.Core.Ecs.Handlers
{
    public abstract class EcsRunHandler
    {
        public EcsWorld World;
        public bool IsRun;
        protected EcsSystems _initSystems;
        protected EcsSystems _mulliganSystems;
        protected EcsSystems _generalSystems;
        protected EcsSystems _creatureSystems;
        protected EcsSystems _cardSystems;
        protected EcsSystems _castSystems;
        protected EcsSystems _turnSystems;
        protected EcsSystems _triggerSystems;
        protected EcsSystems _ruleSystems;
        protected EcsSystems _abilitySystems;
        protected EcsSystems _delSystems;


        protected EcsData _data;
        protected List<EcsSystems> _allSystems;
        public EcsRunHandler(IGameStateContext state)
        {
            World = new EcsWorld();
            _data = new EcsData();
            _allSystems = new List<EcsSystems>();
            _initSystems     = new EcsSystems(World, state);
            _mulliganSystems = new EcsSystems(World, state);
            _generalSystems  = new EcsSystems(World, state);
            _creatureSystems = new EcsSystems(World, state);
            _cardSystems     = new EcsSystems(World, state);
            _castSystems     = new EcsSystems(World, state);
            _turnSystems     = new EcsSystems(World, state);
            _triggerSystems  = new EcsSystems(World, state);
            _ruleSystems     = new EcsSystems(World, state);
            _abilitySystems  = new EcsSystems(World, state);
            _delSystems      = new EcsSystems(World, state);

            _mulliganSystems
                .Add(new RunMulliganReplaceSystem())
                .Add(new RunMulliganReadySystem())
                .Add(new RunMulliganSyncSystem())
                ;

            _initSystems
                .Add(new InitPlayerSystem())
                .Add(new InitDeckSystem())
                .Add(new InitPveOpponentSystem())   // PvE: колода/рука/командир ИИ из энкаунтера (в MP — no-op)
                .Add(new InitTurnSystem())
                .Add(new InitMulliganSystem())
                ;

            _turnSystems
                .Add(new RunFirstTurnStartSystem())
                .Add(new HandDesyncCanarySystem())      // ВРЕМЕННО: лог руки/колоды по ключам на старте хода (диагностика дрейфа)
                .Add(new RunTurnStartSystem())          // каскад начала хода: ресурсы/добор/OnTurnStart (по StartTurnState)
                .Add(new DurationAuraTickSystem())
                .Add(new CardPlayedBridgeSystem())   // CardCastEvent → CardPlayedEvent (оживляет счётчики/Попадос)
                .Add(new MatchCounterTrackerSystem())
                .Add(new LastPlayedSpellTrackerSystem())
                .Add(new CreatureTimerTickSystem())
                .Add(new CharmTimerTickSystem())   // таймер жизни чар (тик в конце хода → DeadTag)
                .Add(new TemporaryManaRefundSystem())
                .Add(new TempControlRevertSystem())
                .Add(new TurnTimerSystem())
                .Add(new RunRequestEndTurnSystem())   // ручной конец хода (UI/дев) → EndTurnRequestEvent
                ;

            _generalSystems
                // --- Создание сущностей карт по CreateCardEvent ---
                .Add(new CreateCardSystem())
                // --- Синхронизация: сбор действий (актив) + воспроизведение снапшотов (пассив) ---
                .Add(new CollectActionSystem())
                .Add(new ReplayActionSystem())
                // --- Доступность карт для UI ---
                .Add(new CardAffordabilitySystem())
                // --- Замена добора (Адовый червь): перехват ПЕРЕД обычным добором ---
                .Add(new RunDrawReplacementSystem())
                // --- Раскопка-эффект (DiscoverEffect): окно выбора → карта в руку, синк через ActionCardPicked ---
                .Add(new RunDiscoverSystem())
                // --- Добор + UI-трансляция ---
                .Add(new DrawCardSystem())
                .Add(new HandUISystem())
                // --- Спаун визуала существа на доске ---
                .Add(new SpawnCreatureViewSystem())
                // --- Гейт «призыв → OnCast» (#2): CardCastEvent после анимации призыва ---
                .Add(new RunPendingOnCastSystem())
                // --- Командная подкраска вью (свои/чужие): реактивно к смене владельца (кража контроля) ---
                .Add(new TeamTintSystem())
                // AuraRecalcSystem УДАЛЁН: ауры теперь реактивные (модификатор-стек AddModifier/RemoveModifier),
                // а его покадровый Value=Base затирал бы модификаторы. Легаси AuraSource/TargetMask не используются.
                // --- Выбор/движение/атака существ на борде (input → MoveRequestEvent/AttackRequestEvent) ---
                .Add(new RunSelectCellSystem())
                // --- Движение существ ---
                .Add(new MoveSystem())
                // --- Бой ---
                .Add(new AttackSystem())
                .Add(new ReflectDamageSystem())   // Вуду-будду: БЛОК урона владельцу на его ходу (до TakeDamage)
                .Add(new TakeDamageSystem())
                .Add(new DieSystem())
                .Add(new CharmDieSystem())   // уничтожение чар с DeadTag (таймер истёк) → грав/лимбо + CreatureDiedEvent
                .Add(new RunLeaveBoardSystem())   // баунс/баниш/замешивание (LeaveBoardEvent) → рука/колода/грав/лимбо
                .Add(new RunCommanderCooldownSystem())   // кулдаун командира на ЛЮБОЙ возврат в руку (смерть/баунс), а не только смерть
                // --- Конец матча: HP игрока ≤ 0 после оседания каскада → победа/поражение/ничья ---
                .Add(new GameOverCheckSystem())
                // --- Отображение статов существ (HP/атака/скорость) в их CreatureView ---
                .Add(new CreatureStatsViewSystem())
                // --- Отображение HP/ресурсов игроков (аватар врага + панель локального) ---
                .Add(new PlayerStatsViewSystem())
                // --- Утилиты ---
                .Add(new BurnCardSystem())
                // --- Тех-читы (выдать ресурсы по дев-кнопке) ---
                .Add(new DebugCheatSystem())
                // --- Драг-розыгрыш существа «под пальцем» (превью-модель; мост UI↔мир через bus-события).
                //     В КОНЦЕ general: его CellClickEvent не видит RunSelectCellSystem (уже отработал в этом
                //     кадре), но видят роутер (_abilitySystems) и RunSelectCellBoardSystem (_creatureSystems). ---
                .Add(new CreatureDragPreviewSystem())
                ;

            // ── TODO (новый card-cast роутер): CardCastEvent → подсистемы существо/заклинание/чары
            //    → finish-cast → событие для триггеров. Реализуем отдельным шагом. ──

            _creatureSystems
                .Add(new RunSelectCellBoardSystem())
                .Add(new RunMoveCardToBoardSystem())
                .Add(new RunMoveCardToGraveSystem())
                .Add(new RunInvokeCreatureSystem())
                .Add(new RepositionViewSystem())   // визуальный «переезд» по ViewRepositionRequest (Бешеная бабка)
                ;

            // ── Event-driven пайплайн способностей ──
            // CardCast → триггеры вешают AbilityCastEvent → проверка правил → AbilityQueue → резолв по одной.
            _abilitySystems
                // PvE-мозг ПЕРВЫМ в группе: его однокадровые RequestCardCastEvent/CellClickEvent должны
                // родиться ДО потребителей в кадре (роутер — ниже в этой группе; RunSelectCellBoardSystem —
                // в _creatureSystems после), иначе DelHere сотрёт их до обработки (гонка как у форс-плея).
                .Add(new RunAiTurnSystem())
                .Add(new PveOpponentCardPlayUISystem())   // PvE: выкат «оппонент разыграл карту» (в MP — no-op)
                .Add(new AutoCastSystem())            // авто-розыгрыш созданных карт (Фокус-покус) → RequestCardCast
                .Add(new RunCastRouterSystem())
                .Add(new RunCheckAbilityRulesSystem())
                .Add(new RunAbilityTargetingSystem())
                .Add(new RunAbilityTargetSelectionSystem())   // Selected по доске (клики)
                .Add(new RunAbilityPickSelectionSystem())     // Selected из колоды/руки/кладбища (окно выбора)
                .Add(new RunResolveAbilityQueueSystem())
                .Add(new RunChainSystem())                 // составные (цепочечные) способности
                ;

            // Завершение хода и активация — ПОСЛЕ ability/creature-систем, чтобы видеть реальное
            // состояние пайплайна (очередь способностей/анимации) при гейте «осело ли».
            _castSystems
                .Add(new RunActivateSystem())      // навешивает ActiveState, когда каскад начала хода осел
                .Add(new EndTurnRequestSystem())   // конец хода: лок → OnTurnEnd → снапшот EndTurn + снять ActiveState
                ;

            _delSystems
                .DelHere<MatchStartEvent>()
                .DelHere<TurnStartEvent>()
                .DelHere<TurnEndEvent>()
                .DelHere<DieEvent>()
                .DelHere<CastEvent>()
                .DelHere<InvokeEvent>()
                .DelHere<CellClickEvent>()
                .DelHere<AttackHitEvent>()
                .DelHere<CardPickResultComponent>()
                // --- Новые события каст-пайплайна ---
                .DelHere<RequestCardCastEvent>()
                .DelHere<DeclineCardCastEvent>()
                .DelHere<MoveCardToGraveEvent>()
                .DelHere<MoveCardToBoardEvent>()
                // AbilityCastEvent НЕ авто-удаляем: его потребляет RunCheckAbilityRulesSystem
                // (может быть поставлен в _creatureSystems после _abilitySystems — доживёт до след. кадра).
                ;

            _allSystems.Add(_initSystems);
            _allSystems.Add(_mulliganSystems);
            _allSystems.Add(_turnSystems);
            _allSystems.Add(_ruleSystems);
            _allSystems.Add(_generalSystems);
            _allSystems.Add(_cardSystems);
            _allSystems.Add(_abilitySystems);
            _allSystems.Add(_creatureSystems);
            _allSystems.Add(_castSystems);
            _allSystems.Add(_triggerSystems);
            _allSystems.Add(_delSystems);

#if UNITY_EDITOR
            _generalSystems.Add(new Leopotam.EcsLite.UnityEditor.EcsWorldDebugSystem());
#endif
        }

        public virtual void Init(params object[] injectData)
        {
            for (int i = 0; i < _allSystems.Count; i++)
            {
                var systems = _allSystems[i];
                systems.Inject(injectData);
                systems.Init();
            }

            _systemsCount = _allSystems.Count;
        }

        protected int _systemsCount;

        public virtual void Run()
        {
            for (int i = 0; i < _systemsCount; i++)
            {
                try
                {
                    _allSystems[i].Run();
                }
                catch (System.Exception e)
                {
                    // Изоляция: исключение в одной группе систем не должно прерывать остальные группы
                    // кадра (иначе один сбой ломает весь пайплайн — «всё ссыпается»).
                    UnityEngine.Debug.LogError($"[EcsRunHandler] system group {i} threw: {e}");
                }
            }
        }
        public virtual void LateRun()
        {

        }
        public virtual void FixedRun()
        {

        }

        public virtual void Dispose()
        {
            _allSystems.ForEach(_x => _x.Destroy());
            // Конец сессии: снимаем все подписки шины (триггеры/условия способностей).
            // Карта в игре не удаляется (сжигание = кладбище), поэтому пер-карта отписка не нужна.
            Game.Core.Events.GameEventBus.Clear();
            Game.Core.Ecs.Components.CastMultiplierService.Clear();   // матч-множители частоты (Временная петля)
            Game.Core.Ecs.Components.MatchState.Clear();              // статус конца матча

            World.Destroy();
            World = null;
        }

        public static EcsRunHandler Create(IGameStateContext context)
        {
            return context.IsServer ? new ServerRunHandler(context) : new ClientRunHandler(context);
        }
    }

    public class EcsData
    {

    }
}
