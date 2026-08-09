using System;
using System.Collections.Generic;

namespace Game.Core.Network
{
    /// <summary>
    /// Статический мост «здоровье соединения → ECS» (паттерн ActionQueue): Fusion-колбэки и RPC пишут сюда
    /// (могут звать из другого потока), NetConnectionWatchSystem/TurnChecksumSystem читают в главном.
    /// Содержит: время последнего сигнала от пира (heartbeat/действие/чексумма), флаги «оппонент вышел» /
    /// «сами отвалились», инбокс чексумм границ ходов. Armed=true после первого сигнала пира — так watchdog
    /// сам не активируется в PvE/оффлайне (там сигналов нет). Чистится ClearForBattle() на старте боя.
    /// </summary>
    public static class NetHealth
    {
        static readonly object _lock = new object();
        static readonly Queue<(int BoundaryTurn, ulong Hash)> _checksums = new Queue<(int, ulong)>();

        static long _lastPeerSignalTicks;   // DateTime.UtcNow.Ticks последнего сигнала пира
        static long _opponentLeftTicks;     // 0 = не выходил
        static long _selfDisconnectedTicks; // 0 = соединение живо
        static bool _armed;                 // был ли ХОТЬ ОДИН сигнал пира (MP-бой реально идёт)

        // ── запись (Fusion-колбэки / RPC, любой поток) ───────────────────────────

        /// <summary>Любой сигнал от пира: heartbeat, action-снапшот, чексумма. Взводит watchdog (Armed).</summary>
        public static void NotePeerSignal()
        {
            lock (_lock)
            {
                _lastPeerSignalTicks = DateTime.UtcNow.Ticks;
                _armed = true;
            }
        }

        public static void NoteOpponentLeft()
        {
            lock (_lock) { if (_opponentLeftTicks == 0) _opponentLeftTicks = DateTime.UtcNow.Ticks; }
        }

        public static void NoteSelfDisconnected()
        {
            lock (_lock) { if (_selfDisconnectedTicks == 0) _selfDisconnectedTicks = DateTime.UtcNow.Ticks; }
        }

        /// <summary>
        /// Оппонент ВЕРНУЛСЯ в матч (реконнект: пришёл RPC_RequestReconnect). Снимаем флаг ухода — иначе
        /// watchdog добьёт матч техпобедой по grace-таймеру, хотя пир уже снова на связи. Заодно считаем
        /// это сигналом пира (сброс отсчёта тишины) — баннер «соперник не отвечает» уйдёт сам.
        /// </summary>
        public static void NoteOpponentReturned()
        {
            lock (_lock)
            {
                _opponentLeftTicks = 0;
                _lastPeerSignalTicks = DateTime.UtcNow.Ticks;
                _armed = true;
            }
        }

        public static void EnqueueChecksum(int boundaryTurn, ulong hash)
        {
            lock (_lock) _checksums.Enqueue((boundaryTurn, hash));
        }

        // ── чтение (главный поток, ECS) ──────────────────────────────────────────

        public static bool Armed { get { lock (_lock) return _armed; } }

        /// <summary>Секунд с последнего сигнала пира (0, если сигналов ещё не было).</summary>
        public static double SecondsSincePeerSignal
        {
            get
            {
                lock (_lock)
                {
                    if (_lastPeerSignalTicks == 0) return 0;
                    return TimeSpan.FromTicks(DateTime.UtcNow.Ticks - _lastPeerSignalTicks).TotalSeconds;
                }
            }
        }

        /// <summary>Секунд с момента выхода оппонента из сессии; меньше 0, если не выходил.</summary>
        public static double SecondsSinceOpponentLeft
        {
            get
            {
                lock (_lock)
                {
                    if (_opponentLeftTicks == 0) return -1;
                    return TimeSpan.FromTicks(DateTime.UtcNow.Ticks - _opponentLeftTicks).TotalSeconds;
                }
            }
        }

        /// <summary>Секунд с момента потери СВОЕГО соединения; меньше 0, если живо.</summary>
        public static double SecondsSinceSelfDisconnected
        {
            get
            {
                lock (_lock)
                {
                    if (_selfDisconnectedTicks == 0) return -1;
                    return TimeSpan.FromTicks(DateTime.UtcNow.Ticks - _selfDisconnectedTicks).TotalSeconds;
                }
            }
        }

        public static bool TryDequeueChecksum(out int boundaryTurn, out ulong hash)
        {
            lock (_lock)
            {
                if (_checksums.Count == 0) { boundaryTurn = 0; hash = 0; return false; }
                var (t, h) = _checksums.Dequeue();
                boundaryTurn = t; hash = h;
                return true;
            }
        }

        /// <summary>Сброс на старте боя (NetConnectionWatchSystem.Init) — флаги прошлой сессии не протекают.</summary>
        public static void ClearForBattle()
        {
            lock (_lock)
            {
                _checksums.Clear();
                _lastPeerSignalTicks = 0;
                _opponentLeftTicks = 0;
                _selfDisconnectedTicks = 0;
                _armed = false;
            }
        }
    }
}
