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

            _initSystems
                .Add(new InitLocalPlayerSystem())
                .Add(new InitTurnSystem())
                .Add(new InitAbilityQueueSystem())
                ;

            _turnSystems
                // --- Ожидание завершения способностей начала хода ---
                .Add(new TurnStartReadySystem())
                // --- Ресурсы и взятие карты когда игрок получил управление ---
                .Add(new TurnStartResourceSystem())
                // --- Таймер хода (только в фазе PlayerTurn) ---
                .Add(new TurnTimerSystem())
                // --- Ожидание завершения способностей конца хода ---
                .Add(new TurnEndReadySystem())
                // --- Передача хода следующему игроку ---
                .Add(new TurnTransferSystem())
                ;

            _generalSystems
                // --- Условия способностей ---
                .Add(new CheckConditionSystem())
                // --- Доступность карт для розыгрыша (UI) ---
                .Add(new CardAffordabilitySystem())
                // --- Ввод ---
                .Add(new RunSelectCellSystem())
                .Add(new DrawCardSystem())
                // --- Розыгрыш карт ---
                .Add(new CastCardSystem())
                // --- Движение существ ---
                .Add(new MoveSystem())
                // --- Бой ---
                .Add(new AttackSystem())
                .Add(new TakeDamageSystem())
                .Add(new DieSystem())
                // --- Утилиты ---
                .Add(new BurnCardSystem())
                // --- Конец хода ---
                .Add(new EndTurnRequestSystem())
                ;

            _cardSystems
                .Add(new RunCastCardSystem())
                .Add(new RunTurnStartCardSystem())
                .Add(new RunTurnEndCardSystem())
                .Add(new RunDieCardSystem())
                ;

            _abilitySystems
                .Add(new UnlockAbilityQueueSystem())
                .Add(new AbilityQueueSystem())
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
                .DelHere<TurnStartEvent>()
                .DelHere<TurnEndEvent>()
                .DelHere<TurnTransferEvent>()
                .DelHere<DieEvent>()
                .DelHere<CastEvent>()
                .DelHere<ResolveAbilityEvent>()
                .DelHere<CellClickEvent>()
                .DelHere<AttackHitEvent>()
                ;

            _allSystems.Add(_initSystems);
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