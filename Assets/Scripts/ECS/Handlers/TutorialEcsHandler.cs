using System.Collections.Generic;
using Game.Core.Ecs.Systems;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;
using Leopotam.EcsLite.ExtendedSystems;
using Leopotam.EcsLite.Di;

namespace Game.Core.Ecs.Handlers
{
    // === class (ECS handler) ===
    /// <summary>
    /// ECS-хендлер ТУТОРИАЛА — полностью отвязан от боевого EcsRunHandler (не наследует): свой поднабор
    /// тех же систем-кирпичей БЕЗ сети (Collect/Replay/Photon), БЕЗ мулигана (руку раздаёт директор),
    /// БЕЗ ИИ-мозга (оппонент пасует/скриптуется директором) и БЕЗ таймера хода.
    /// Работает на PvE-механизме (PveMode.Enabled + туториальный энкаунтер): фиксированные колоды через
    /// PveEncounterConfig.PlayerDeck / колоду ИИ, гейты TurnGate/EndTurn — PvE-ветки.
    /// Сценарий шагов и подсказки ведёт TutorialDirectorSystem.
    /// </summary>
    public sealed class TutorialEcsHandler
    {
        public EcsWorld World;
        readonly List<EcsSystems> _all = new();
        int _count;

        public TutorialEcsHandler(IGameStateContext state)
        {
            World = new EcsWorld();

            var init = new EcsSystems(World, state);
            init
                .Add(new InitPlayerSystem())        // PvE-ветка: человек + ИИ
                .Add(new InitDeckSystem())          // сюжетная колода игрока из энкаунтера (PlayerDeck)
                .Add(new InitPveOpponentSystem())   // колода «оппонента»-груши
                .Add(new InitTurnSystem())
                ;

            var turn = new EcsSystems(World, state);
            turn
                .Add(new RunTurnStartSystem())
                .Add(new TutorialDirectorSystem())  // сетап (рука/груша/первый ход) + шаги/хинты + автопас ИИ
                .Add(new DurationAuraTickSystem())
                .Add(new CardPlayedBridgeSystem())
                .Add(new MatchCounterTrackerSystem())
                .Add(new LastPlayedSpellTrackerSystem())
                .Add(new CreatureTimerTickSystem())
                .Add(new CharmTimerTickSystem())
                .Add(new TemporaryManaRefundSystem())
                .Add(new TempControlRevertSystem())
                .Add(new RunRequestEndTurnSystem())
                ;

            var general = new EcsSystems(World, state);
            general
                .Add(new CreateCardSystem())
                .Add(new CardAffordabilitySystem())
                .Add(new RunDrawReplacementSystem())
                .Add(new RunDiscoverSystem())
                .Add(new DrawCardSystem())
                .Add(new HandUISystem())
                .Add(new SpawnCreatureViewSystem())
                .Add(new RunPendingOnCastSystem())
                .Add(new TeamTintSystem())
                .Add(new RunSelectCellSystem())
                .Add(new MoveSystem())
                .Add(new AttackSystem())
                .Add(new ReflectDamageSystem())
                .Add(new TakeDamageSystem())
                .Add(new DieSystem())
                .Add(new CharmDieSystem())
                .Add(new RunLeaveBoardSystem())
                .Add(new RunCommanderCooldownSystem())
                .Add(new GameOverCheckSystem())
                .Add(new CreatureStatsViewSystem())
                .Add(new PlayerStatsViewSystem())
                .Add(new BurnCardSystem())
                .Add(new DebugCheatSystem())
                .Add(new CreatureDragPreviewSystem())
                ;

            var ability = new EcsSystems(World, state);
            ability
                .Add(new AutoCastSystem())
                .Add(new RunCastRouterSystem())
                .Add(new RunCheckAbilityRulesSystem())
                .Add(new RunAbilityTargetingSystem())
                .Add(new RunAbilityTargetSelectionSystem())
                .Add(new RunAbilityPickSelectionSystem())
                .Add(new RunResolveAbilityQueueSystem())
                .Add(new RunChainSystem())
                ;

            var creature = new EcsSystems(World, state);
            creature
                .Add(new RunSelectCellBoardSystem())
                .Add(new RunMoveCardToBoardSystem())
                .Add(new RunMoveCardToGraveSystem())
                .Add(new RunInvokeCreatureSystem())
                .Add(new RepositionViewSystem())
                ;

            var cast = new EcsSystems(World, state);
            cast
                .Add(new RunActivateSystem())
                .Add(new EndTurnRequestSystem())    // PvE-ветки: пускает ИИ + чередует ходы
                ;

            var del = new EcsSystems(World, state);
            del
                .DelHere<Game.Core.Ecs.Components.MatchStartEvent>()
                .DelHere<Game.Core.Ecs.Components.TurnStartEvent>()
                .DelHere<Game.Core.Ecs.Components.TurnEndEvent>()
                .DelHere<Game.Core.Ecs.Components.DieEvent>()
                .DelHere<Game.Core.Ecs.Components.CastEvent>()
                .DelHere<Game.Core.Ecs.Components.InvokeEvent>()
                .DelHere<Game.Core.Ecs.Components.CellClickEvent>()
                .DelHere<Game.Core.Ecs.Components.AttackHitEvent>()
                .DelHere<Game.Core.Ecs.Components.CardPickResultComponent>()
                .DelHere<Game.Core.Ecs.Components.RequestCardCastEvent>()
                .DelHere<Game.Core.Ecs.Components.DeclineCardCastEvent>()
                .DelHere<Game.Core.Ecs.Components.MoveCardToGraveEvent>()
                .DelHere<Game.Core.Ecs.Components.MoveCardToBoardEvent>()
                ;

            // Порядок групп — как в бою (init → turn → general → ability → creature → cast → del).
            _all.Add(init);
            _all.Add(turn);
            _all.Add(general);
            _all.Add(ability);
            _all.Add(creature);
            _all.Add(cast);
            _all.Add(del);
        }

        public void Init(params object[] injectData)
        {
            foreach (var s in _all)
            {
                s.Inject(injectData);
                s.Init();
            }
            _count = _all.Count;
        }

        public void Run()
        {
            for (int i = 0; i < _count; i++)
            {
                try { _all[i].Run(); }
                catch (System.Exception e)
                {
                    UnityEngine.Debug.LogError($"[TutorialEcsHandler] system group {i} threw: {e}");
                }
            }
        }

        public void Dispose()
        {
            _all.ForEach(s => s.Destroy());
            Game.Core.Events.GameEventBus.Clear();
            Game.Core.Ecs.Components.CastMultiplierService.Clear();
            Game.Core.Ecs.Components.MatchState.Clear();
            World.Destroy();
            World = null;
        }
    }
}
