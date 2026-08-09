using System.Collections.Generic;
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
    /// ДЕТЕКТ ДЕСИНКА (MP): на каждой границе хода оба клиента считают чексумму зеркалируемого состояния
    /// и обмениваются ею (RPC_ReportChecksum). Расхождение → NetDesyncDetectedEvent + LogError (разбор по
    /// [Canary]/[BoardCanary] логам; позже — триггер ресинка).
    ///
    /// ТОЧКА РАСЧЁТА — «конец хода N» (единственная логически одинаковая точка обоих клиентов, т.к.
    /// TurnStartedEvent каждый клиент видит только для СВОИХ ходов):
    ///   • клиент, АКТИВНЫЙ в ходу N — на EndTurnNetEvent (свой ход завершён и осел);
    ///   • клиент, ПАССИВНЫЙ в ходу N — на TurnStartedEvent(N+1) (все действия N дореплеены; свой каскад
    ///     старта ещё не тронул хэшируемое состояние — золото/мана в хэш не входят, добор ещё не применён).
    /// Каждый клиент считает КАЖДУЮ границу ровно один раз (дедуп _lastBoundaryHashed).
    ///
    /// В ХЭШЕ только НАДЁЖНО зеркалируемое: существа на доске (ключ|атака|HP|позиция|владелец), HP аватаров,
    /// руки и колоды как множества ключей. НЕ входит: золото/мана (пассив не зеркалит доход), скорость/лимит
    /// атак (восстанавливаются в разное время у актива/пассива), чары с таймерами (тикают локально).
    /// Сортировка строк Ordinal — порядок итерации ECS-фильтров не гарантирован между клиентами.
    /// PvE: _photon == null → выходим сразу, ничего не считаем.
    /// </summary>
    public sealed class TurnChecksumSystem : IEcsInitSystem, IEcsRunSystem, IEcsDestroySystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsCustomInject<PhotonRunHandler> _photon = default;

        // «Мир осел»: гейт EndTurnRequestSystem.AbilitiesPending НЕ ждёт применения урона/смертей
        // (TakeDamageEvent обрабатывается general-фазой ПОСЛЕ turn-фазы) — хэшировать синхронно в момент
        // EndTurnNetEvent значит поймать состояние «до урона» от OnTurnEnd-способностей → ложный десинк-алярм.
        // Поэтому расчёт откладывается до оседания (обычно 1-3 кадра; см. GameOverCheckSystem.CascadeBusy).
        readonly EcsFilterInject<Inc<TakeDamageEvent>>       _takeDamage    = default;
        readonly EcsFilterInject<Inc<AttackHitEvent>>        _attackHit     = default;
        readonly EcsFilterInject<Inc<MovingTag>>             _moving        = default;
        readonly EcsFilterInject<Inc<AttackAnimPendingTag>>  _attackAnim    = default;
        readonly EcsFilterInject<Inc<AbilityCastEvent>>      _abilityCast   = default;
        readonly EcsFilterInject<Inc<AbilityTargetingState>> _abilityTarget = default;
        readonly EcsFilterInject<Inc<AbilityQueuedState>>    _abilityQueued = default;
        readonly EcsFilterInject<Inc<PendingOnCastComponent>> _pendingOnCast = default;
        readonly EcsFilterInject<Inc<DeadTag, BoardTag>>     _deadOnBoard   = default;


        int _currentTurn;
        int _lastBoundaryHashed;
        readonly Dictionary<int, ulong> _localHashes  = new();
        readonly Dictionary<int, ulong> _remoteHashes = new();

        public void Init(IEcsSystems systems)
        {
            GameEventBus.Subscribe<TurnStartedEvent>(OnTurnStarted);
            GameEventBus.Subscribe<EndTurnNetEvent>(OnEndTurnNet);
        }

        public void Destroy(IEcsSystems systems)
        {
            GameEventBus.Unsubscribe<TurnStartedEvent>(OnTurnStarted);
            GameEventBus.Unsubscribe<EndTurnNetEvent>(OnEndTurnNet);
        }

        void OnTurnStarted(TurnStartedEvent e)
        {
            if (e.TurnNumber > _currentTurn) _currentTurn = e.TurnNumber;

            // Хвост своего EndTurnNet-расчёта не осел за целый ход оппонента (не должно) — форс.
            if (_pendingBoundary > 0) { ComputeAndPublish(_pendingBoundary); _pendingBoundary = -1; }

            // Границу N-1 (мы были ПАССИВОМ) считаем СИНХРОННО ПРЯМО ЗДЕСЬ: добор нового хода ещё НЕ применён
            // (DrawCardEvent обработается позже в кадре). Отложить на кадр = захватить добор, которого у пира
            // в этой границе нет → ложный десинк каждый ход (боевой лог 2026-07-27: b4 и b5 с одинаковым хэшем).
            if (CanHash(e.TurnNumber - 1))
            {
                _lastBoundaryHashed = e.TurnNumber - 1;
                ComputeAndPublish(e.TurnNumber - 1);
            }
        }

        // Свой ход завершён (мы АКТИВ): расчёт ОТЛОЖЕН до оседания — урон OnTurnEnd-способностей ещё не применён
        // (general-фаза идёт после turn-фазы). До первого действия оппонента (RTT + его каскад) успеваем.
        void OnEndTurnNet(EndTurnNetEvent e)
        {
            if (!CanHash(_currentTurn)) return;
            _lastBoundaryHashed = _currentTurn;
            _pendingBoundary = _currentTurn;
        }

        int _pendingBoundary = -1;   // граница, ждущая оседания мира перед расчётом

        bool CanHash(int boundary)
        {
            // boundary < 2: прогрев — зеркало руки/колоды оппонента строится асинхронно после мулигана
            // (боевой лог: Canary t1 P2 deck=0 hand=0 → граница 1 всегда «расходилась»).
            if (boundary < 2 || boundary <= _lastBoundaryHashed) return false;
            if (_photon.Value == null || MatchState.IsOver) return false;
            return true;
        }

        public void Run(IEcsSystems systems)
        {
            // Чужие чексуммы из RPC-инбокса (могут прийти раньше или позже нашего расчёта той же границы).
            while (NetHealth.TryDequeueChecksum(out int boundary, out ulong hash))
            {
                _remoteHashes[boundary] = hash;
                TryCompare(boundary);
            }

            // Отложенный расчёт границы: ждём применения урона/смертей (см. комментарий у фильтров).
            if (_pendingBoundary > 0 && WorldSettled())
            {
                ComputeAndPublish(_pendingBoundary);
                _pendingBoundary = -1;
            }
        }

        readonly Dictionary<int, string> _localDump = new();   // канонические строки границы — дамп при десинке
        int _consecutiveMismatches;

        void ComputeAndPublish(int boundary)
        {
            // Зеркало какого-то игрока ещё не материализовано (рука+колода пусты — старт матча/медленная сеть):
            // сравнивать нечего, пропускаем границу (у пира несравненная запись сама зачистится PruneOld).
            var world = _world.Value;
            var handPool = world.GetPool<HandComponent>();
            var deckPool = world.GetPool<DeckComponent>();
            foreach (var pe in world.Filter<PlayerComponent>().Inc<HandComponent>().End())
            {
                int h = handPool.Get(pe).Count;
                int d = deckPool.Has(pe) ? deckPool.Get(pe).Count : 0;
                if (h + d == 0)
                {
                    UnityEngine.Debug.Log($"[Checksum] boundary={boundary} пропущена: зеркало игрока ещё не построено");
                    return;
                }
            }

            var lines = new List<string>();
            ulong hash = WorldStateHash.Compute(_world.Value, lines);

            // ПОРЯДОК колод — в ДАМП, но НЕ в хэш (чексумма сравнивает колоды как множества осознанно).
            // Эффектные доборы (DrawCardEffect, ре-ран на обоих) берут ВЕРХ колоды локально: расхождение
            // ПОРЯДКА даёт разные добранные карты при ещё сошедшихся множествах — в дампе это было
            // невидимо (подозрение десинка b7 матча 2026-07-28, «что-то с добором»). Хэш уже посчитан —
            // строки ниже попадают только в лог для диффа с пиром.
            var netPool    = world.GetPool<NetworkEntityComponent>();
            var playerPool = world.GetPool<PlayerComponent>();
            var orderLines = new List<string>(2);
            var sb = new System.Text.StringBuilder();
            foreach (var pe in world.Filter<PlayerComponent>().Inc<DeckComponent>().End())
            {
                ref var deck = ref deckPool.Get(pe);
                if (deck.CardEntities == null) continue;
                sb.Clear();
                sb.Append("O|").Append(playerPool.Get(pe).PlayerId).Append('|');
                for (int i = 0; i < deck.CardEntities.Count; i++)
                {
                    int c = deck.CardEntities[i];
                    sb.Append(netPool.Has(c) ? netPool.Get(c).NetworkEntityKey : c.ToString()).Append(' ');
                }
                orderLines.Add(sb.ToString());
            }
            lines.AddRange(orderLines);

            _localDump[boundary] = string.Join("\n", lines);
            _localHashes[boundary] = hash;
            _photon.Value?.SendChecksum(boundary, hash);
            // Порядок колод логируем на КАЖДОЙ границе: при десинке важно найти границу, где порядок
            // разошёлся, пока множества ещё сходились (дифф O-строк двух клиентов той же границы).
            UnityEngine.Debug.Log($"[Checksum] boundary={boundary} hash={hash:X16}\n{string.Join("\n", orderLines)}");
            TryCompare(boundary);
        }

        bool WorldSettled()
            => _takeDamage.Value.GetEntitiesCount()    == 0
            && _attackHit.Value.GetEntitiesCount()     == 0
            && _moving.Value.GetEntitiesCount()        == 0
            && _attackAnim.Value.GetEntitiesCount()    == 0
            && _abilityCast.Value.GetEntitiesCount()   == 0
            && _abilityTarget.Value.GetEntitiesCount() == 0
            && _abilityQueued.Value.GetEntitiesCount() == 0
            && _pendingOnCast.Value.GetEntitiesCount() == 0
            && _deadOnBoard.Value.GetEntitiesCount()   == 0;

        void TryCompare(int boundary)
        {
            if (!_localHashes.TryGetValue(boundary, out var local)) return;
            if (!_remoteHashes.TryGetValue(boundary, out var remote)) return;

            if (local != remote)
            {
                _consecutiveMismatches++;
                string dump = _localDump.TryGetValue(boundary, out var d) ? d : "<нет дампа>";
                UnityEngine.Debug.LogError($"[Checksum] ДЕСИНК на границе хода {boundary} (#{_consecutiveMismatches} подряд): " +
                                           $"local={local:X16} remote={remote:X16}\n--- канонические строки (сравнить с логом пира) ---\n{dump}");
                // Ресинк — только со ВТОРОЙ подряд разошедшейся границы: одиночный тайминг-глитч не гоняет фейд,
                // реальный десинк стабилен и попадёт под ресинк на следующей границе.
                if (_consecutiveMismatches >= 2)
                    GameEventBus.Publish(new NetDesyncDetectedEvent { BoundaryTurn = boundary, LocalHash = local, RemoteHash = remote });
            }
            else
            {
                _consecutiveMismatches = 0;
            }

            _localHashes.Remove(boundary);
            _remoteHashes.Remove(boundary);
            _localDump.Remove(boundary);
            PruneOld(boundary);
        }

        // Границы сильно старше текущей — недосравненный мусор (пир мог не прислать/мы не считали).
        void PruneOld(int newest)
        {
            _scratchKeys.Clear();
            foreach (var k in _localHashes.Keys)  if (k < newest - 6) _scratchKeys.Add(k);
            foreach (var k in _remoteHashes.Keys) if (k < newest - 6 && !_scratchKeys.Contains(k)) _scratchKeys.Add(k);
            foreach (var k in _scratchKeys) { _localHashes.Remove(k); _remoteHashes.Remove(k); }
        }
        readonly List<int> _scratchKeys = new();

        // Канонизация/хэш общие с ресинком — см. WorldStateHash (расширять набор полей там).
        ulong ComputeHash() => WorldStateHash.Compute(_world.Value);
    }
}
