using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Shared.Interface;
using Game.Core.Ecs.Systems;
using System.Collections.Generic;
using Leopotam.EcsLite.ExtendedSystems;
using Game.Core.Ecs.Components;

namespace Game.Core.Ecs.Handlers
{
    public abstract class EcsRunHandler
    {
        public EcsWorld World;
        public bool IsRun;
        protected EcsSystems _initSystems;
        protected EcsSystems _mulliganSystems;
        protected EcsSystems _generalSystems;
        protected EcsSystems _cardSystems;
        protected EcsSystems _abilitySystems;
        protected EcsSystems _resolveSystems;
        protected EcsSystems _turnSystems;
        protected EcsSystems _delSystems;


        protected EcsData _data;
        protected List<EcsSystems> _allSystems;
        public EcsRunHandler(IGameStateContext state)
        {
            World = new EcsWorld();
            _data = new EcsData();
            _allSystems = new List<EcsSystems>();
            _initSystems = new EcsSystems(World, state);
            _mulliganSystems = new EcsSystems(World, state);
            _generalSystems = new EcsSystems(World, state);
            _cardSystems = new EcsSystems(World, state);
            _abilitySystems = new EcsSystems(World, state);
            _resolveSystems = new EcsSystems(World, state);
            _turnSystems = new EcsSystems(World, state);
            _delSystems = new EcsSystems(World, state);

            _mulliganSystems 
                .Add(new RunMulliganReplaceSystem())
                .Add(new RunMulliganReadySystem())
                .Add(new RunMulliganSyncSystem()) 
                ;

            _initSystems
                .Add(new InitPlayerSystem()) 
                .Add(new InitDeckSystem())
                .Add(new InitTurnSystem())
                .Add(new InitAbilityQueueSystem())
                .Add(new InitMulliganSystem())
                ;

            _turnSystems
                .Add(new RunFirstTurnStartSystem())
                // --- Ожидание завершения способностей (MatchStart / TurnStart / TurnEnd) ---
                .Add(new PhaseReadySystem())
                // --- Ресурсы и взятие карты когда игрок получил управление ---
                .Add(new TurnStartResourceSystem())
                // --- Таймер хода (только в фазе PlayerTurn) ---
                .Add(new TurnTimerSystem())
                // --- Передача хода следующему игроку ---
                .Add(new TurnTransferSystem())
                ;

            _generalSystems
                // --- Создание сущностей карт по CreateCardEvent (оппонент, токены, раскопка из пула) ---
                .Add(new CreateCardSystem())
                // --- Доступность карт для розыгрыша (UI) ---
                .Add(new CardAffordabilitySystem())
                .Add(new CheckPlayRequirementSystem())
                // --- Ввод ---
                .Add(new CardInputSystem())
                .Add(new RunSelectCellSystem())
                .Add(new DrawCardSystem())
                // --- UI-трансляция взятия карты в руку ---
                .Add(new HandUISystem())
                // --- Выбор карты (раскопка) перед кастом ---
                .Add(new CardPickSelectionSystem())
                // --- Ожидание выбора клетки / цели ---
                .Add(new TargetSelectionSystem())
                // --- Розыгрыш карт ---
                .Add(new RandomTargetSystem())
                .Add(new CastCardSystem())
                // --- Спаун визуала существа на доске ---
                .Add(new SpawnCreatureViewSystem())
                // --- Пересчёт аур (постоянные эффекты чар) до боя ---
                .Add(new AuraRecalcSystem())
                // --- Движение существ ---
                .Add(new MoveSystem())
                // --- Бой ---
                .Add(new AttackSystem())
                .Add(new TakeDamageSystem())
                .Add(new DieSystem())
                // --- Применение эффектов способностей ---
                .Add(new ApplyHealSystem())
                .Add(new ApplyDrawSystem())
                .Add(new ApplyMillSystem())
                .Add(new ApplyDiscardSystem())
                .Add(new ApplyBuffSystem())
                .Add(new ApplyShuffleCardSystem())
                .Add(new ApplyGainManaSystem())
                .Add(new ApplyGainGoldSystem())
                // --- Утилиты ---
                .Add(new BurnCardSystem())
                // --- Конец хода ---
                .Add(new EndTurnRequestSystem())
                // --- Сбор действий активного игрока и отправка оппоненту ---
                .Add(new CollectActionSystem())
                // --- Воспроизведение действий оппонента из очереди снапшотов ---
                .Add(new ReplayActionSystem())
                ;

            _cardSystems
                .Add(new RunCastCardSystem())
                .Add(new RunStartMatchCardSystem())
                .Add(new RunTurnStartCardSystem())
                .Add(new RunTurnEndCardSystem())
                .Add(new RunDieCardSystem())
                ;

            _abilitySystems
                .Add(new UnlockAbilityQueueSystem())
                .Add(new AbilityQueueSystem())
                .Add(new RunAbilityMatchStartSystem())
                .Add(new RunAbilityCastSystem())
                .Add(new RunAbilityTurnStartSystem())
                .Add(new RunAbilityTurnEndSystem())
                .Add(new RunAbilityDieSystem())
                ;

            _resolveSystems
                .Add(new RunResolveAbilityTargetSystem())
                .Add(new RunResolveAbilityFieldSystem())
                .Add(new RunResolveAbilityHitSystem())
                .Add(new RunResolveAbilityEffectSystem())
                .Add(new RunResolveAbilityActiveSystem())
                ;

            _delSystems 
                .DelHere<MatchStartEvent>()
                .DelHere<TurnStartEvent>()
                .DelHere<TurnEndEvent>()
                .DelHere<DieEvent>()
                .DelHere<CastEvent>() 
                .DelHere<OnCastTrigger>()
                .DelHere<ResolveAbilityEvent>()
                .DelHere<ConditionNotMetTag>()
                .DelHere<CellClickEvent>()
                .DelHere<AttackHitEvent>()
                .DelHere<CardPickResultComponent>()
                .DelHere<AbilityChosenTargetComponent>()
                ;

            _allSystems.Add(_initSystems);
            _allSystems.Add(_mulliganSystems);
            _allSystems.Add(_turnSystems);
            _allSystems.Add(_generalSystems);
            _allSystems.Add(_cardSystems);
            _allSystems.Add(_abilitySystems);
            _allSystems.Add(_resolveSystems);
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
                _allSystems[i].Run();
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