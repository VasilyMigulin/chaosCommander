using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Network;
using Game.Core.Photon;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// ПЕРСИСТ МАТЧА для реконнекта (фаза 2): пишет в MatchSessionStore то, чего вернувшийся не сможет
    /// узнать у пира — имя комнаты, свою игровую идентичность (PlayerId/Side) и свои ресурсы.
    ///
    /// Идентичность сохраняется один раз, лениво в Run: система живёт в _turnSystems, и полагаться на то,
    /// что её Init произойдёт после InitPlayerSystem, нельзя — ждём, пока в мире появится локальный игрок.
    /// Прогресс обновляется на границах ходов (там же обновляется метка свежести записи).
    /// Запись чистится по концу матча и по выходу в меню — чтобы следующий запуск не полез в мёртвую комнату.
    /// PvE и оффлайн игнорируются (_photon == null / PveMode).
    /// </summary>
    public sealed class MatchSessionPersistSystem : IEcsInitSystem, IEcsRunSystem, IEcsDestroySystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsCustomInject<PhotonRunHandler> _photon = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsPoolInject<PlayerSideComponent> _sidePool = default;
        readonly EcsPoolInject<GoldComponent> _goldPool = default;
        readonly EcsPoolInject<ManaComponent> _manaPool = default;
        readonly EcsPoolInject<TurnCounterComponent> _counterPool = default;

        bool _identitySaved;
        bool _ratingIdentitySaved;   // MatchIdentity (GUID матча + PlayFabId соперника) приезжает reveal-RPC ПОЗЖЕ ECS-игроков
        int _turnNumber;

        public void Init(IEcsSystems systems)
        {
            GameEventBus.Subscribe<TurnStartedEvent>(OnTurnStarted);
            GameEventBus.Subscribe<EndTurnNetEvent>(OnEndTurn);
            GameEventBus.Subscribe<MatchEndedEvent>(OnMatchEnded);
            GameEventBus.Subscribe<ExitToMenuRequestedEvent>(OnExitToMenu);
        }

        public void Destroy(IEcsSystems systems)
        {
            GameEventBus.Unsubscribe<TurnStartedEvent>(OnTurnStarted);
            GameEventBus.Unsubscribe<EndTurnNetEvent>(OnEndTurn);
            GameEventBus.Unsubscribe<MatchEndedEvent>(OnMatchEnded);
            GameEventBus.Unsubscribe<ExitToMenuRequestedEvent>(OnExitToMenu);
        }

        bool Enabled => _photon.Value != null && !Game.Core.Service.PveMode.Enabled;

        public void Run(IEcsSystems systems)
        {
            if (!Enabled || (_identitySaved && _ratingIdentitySaved)) return;

            if (!TryResolvePlayers(out int myId, out int mySide, out int oppId)) return;

            string room = PhotonInitializer.Instance != null ? PhotonInitializer.Instance.CurrentRoomName : null;
            if (string.IsNullOrEmpty(room)) return;   // имя комнаты ещё не проставлено — попробуем в след. кадре

            // Идентичность матча для рейтинга может опоздать (reveal-RPC) — досохраняем, когда появится.
            bool ratingSet = Game.Core.Service.MatchIdentity.IsSet;
            if (_identitySaved && !ratingSet) return;   // основное сохранено, ждём только рейтинг-часть

            MatchSessionStore.SaveIdentity(room, myId, mySide, oppId,
                ratingSet ? Game.Core.Service.MatchIdentity.MatchId : null,
                ratingSet ? Game.Core.Service.MatchIdentity.OpponentPlayFabId : null);
            _ratingIdentitySaved = ratingSet;
            if (!_identitySaved) SaveProgress();   // сразу зафиксируем стартовые ресурсы (только в первый раз)
            _identitySaved = true;
        }

        void OnTurnStarted(TurnStartedEvent e)
        {
            if (e.TurnNumber > _turnNumber) _turnNumber = e.TurnNumber;
            SaveProgress();   // доход только что начислен
        }

        void OnEndTurn(EndTurnNetEvent e) => SaveProgress();   // после трат за ход

        void OnMatchEnded(MatchEndedEvent e) => Forget("матч завершён");

        void OnExitToMenu(ExitToMenuRequestedEvent e) => Forget("выход в меню");

        void Forget(string reason)
        {
            if (!Enabled) return;
            MatchSessionStore.Clear();
            ReconnectFlow.Clear();
            UnityEngine.Debug.Log($"[MatchStore] запись о матче удалена: {reason}");
        }

        void SaveProgress()
        {
            if (!_identitySaved || !Enabled) return;

            foreach (var pe in _world.Value.Filter<PlayerComponent>().Inc<LocalComponent>().End())
            {
                int gold = 0, goldMax = 0, mana = 0, manaMax = 0, personal = 0;
                if (_goldPool.Value.Has(pe))    { ref var g = ref _goldPool.Value.Get(pe); gold = g.Current; goldMax = g.Max; }
                if (_manaPool.Value.Has(pe))    { ref var m = ref _manaPool.Value.Get(pe); mana = m.Current; manaMax = m.Max; }
                if (_counterPool.Value.Has(pe)) personal = _counterPool.Value.Get(pe).Personal;

                MatchSessionStore.SaveProgress(gold, goldMax, mana, manaMax, personal, _turnNumber);
                return;
            }
        }

        bool TryResolvePlayers(out int myId, out int mySide, out int oppId)
        {
            myId = -1; mySide = 1; oppId = -1;

            foreach (var pe in _world.Value.Filter<PlayerComponent>().Inc<LocalComponent>().End())
            {
                myId = _playerPool.Value.Get(pe).PlayerId;
                if (_sidePool.Value.Has(pe)) mySide = _sidePool.Value.Get(pe).Side;
                break;
            }
            if (myId < 0) return false;

            foreach (var pe in _world.Value.Filter<PlayerComponent>().Inc<RemoteComponent>().End())
            {
                oppId = _playerPool.Value.Get(pe).PlayerId;
                break;
            }
            return oppId >= 0;
        }
    }
}
