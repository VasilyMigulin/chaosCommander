using System.Collections.Generic;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Network;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// УСТОЙЧИВОСТЬ К ОБРЫВУ (MP). Раньше пассив вечно ждал действий пира (ActionQueue пуст → молча выходим
    /// каждый кадр) — при сне телефона/обрыве висли ОБА клиента. Теперь следим за здоровьем соединения:
    ///   • тишина пира (нет heartbeat/действий/чексумм дольше Warn) → баннер «соперник не отвечает» с
    ///     отсчётом; сигнал вернулся → баннер прячем (ConnectionRestoredUIEvent);
    ///   • Fusion зафиксировал выход оппонента (OnPlayerLeft → NetHealth) → окно ожидания → техпобеда;
    ///   • потеряно СВОЁ соединение (OnDisconnected/OnShutdown) → окно → техпоражение (симметрично
    ///     техпобеде оппонента на его клиенте; реконнект — отдельная фаза).
    /// Матч закрывается ШТАТНЫМ путём конца матча (как GameOverCheckSystem): MatchState.IsOver + снятие
    /// ActiveState + InputBlockedEvent + MatchEndedEvent → обычный поп-ап результата.
    /// PvE безопасно: NetHealth.Armed взводится только первым сигналом пира — в оффлайне сигналов нет.
    /// Источники сигналов пира: RPC_Heartbeat (раз в 3с с обоих клиентов), action-снапшоты, чексуммы.
    /// </summary>
    public sealed class NetConnectionWatchSystem : IEcsInitSystem, IEcsRunSystem
    {
        // Тайминги (секунды). Warn — когда показывать баннер тишины; Timeout — тишина до техпобеды;
        // Grace — окно ожидания после зафиксированного Fusion'ом выхода/обрыва (шанс на возврат — фаза реконнекта).
        const float SilenceWarnSeconds    = 12f;
        // ТИШИНА — единственный сигнал об УБИТОМ приложении: процесс умер, соединение штатно не рвалось,
        // и Fusion сообщает об уходе (OnPlayerLeft) только по своему ConnectionTimeout (NetworkProjectConfig,
        // сейчас 60с). Боевой тест 2026-07-30: хост всё это время видел ровно OpponentSilent. Значит порог
        // тишины ОБЯЗАН быть больше ConnectionTimeout — иначе матч закрывался бы техпобедой раньше, чем слот
        // в комнате освободится и вернувшийся физически сможет войти (было 75с → окно возврата ~15с, не хватало).
        const float SilenceTimeoutSeconds = 150f;
        // Окно ожидания после ЗАФИКСИРОВАННОГО ухода. Отсчёт стартует с дропа пира (t≈60с от смерти
        // приложения), к этому моменту вернувшемуся нужны лишь джойн и снэпшот — секунды. Возврат снимает
        // флаг мгновенно (NetHealth.NoteOpponentReturned из RPC_RequestReconnect).
        const float OpponentLeftGraceSeconds = 90f;
        // Свой обрыв не трогаем: приложение живо, реконнект-путь тут не задействован (это фаза Fusion-реконнекта),
        // а результат остаётся согласованным — мы объявим поражение раньше, чем пир победу.
        const float SelfLostGraceSeconds     = 30f;

        readonly EcsFilterInject<Inc<PlayerComponent>> _players = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsPoolInject<ActiveState>     _activePool = default;

        bool _issueActive;
        int  _lastPublishedSecondsLeft = int.MinValue;

        public void Init(IEcsSystems systems) => NetHealth.ClearForBattle();   // флаги прошлой сессии не протекают

        public void Run(IEcsSystems systems)
        {
            if (MatchState.IsOver) return;

            // Приоритет: свой обрыв > выход оппонента > тишина.
            double selfLost = NetHealth.SecondsSinceSelfDisconnected;
            if (selfLost >= 0)
            {
                Escalate(ConnectionIssueKind.ConnectionLost, SelfLostGraceSeconds - selfLost, MatchResult.Lose);
                return;
            }

            double oppLeft = NetHealth.SecondsSinceOpponentLeft;
            if (oppLeft >= 0)
            {
                Escalate(ConnectionIssueKind.OpponentLeft, OpponentLeftGraceSeconds - oppLeft, MatchResult.Win);
                return;
            }

            if (!NetHealth.Armed) return;   // MP-сигналов ещё не было (PvE/пре-старт) — watchdog спит

            double silence = NetHealth.SecondsSincePeerSignal;
            if (silence > SilenceWarnSeconds)
            {
                Escalate(ConnectionIssueKind.OpponentSilent, SilenceTimeoutSeconds - silence, MatchResult.Win);
                return;
            }

            // Сигналы идут (или вернулись) — если показывали баннер, спрятать.
            if (_issueActive)
            {
                _issueActive = false;
                _lastPublishedSecondsLeft = int.MinValue;
                GameEventBus.Publish(new ConnectionRestoredUIEvent());
                UnityEngine.Debug.Log("[NetWatch] сигналы пира восстановились — баннер снят");
            }
        }

        void Escalate(ConnectionIssueKind kind, double secondsLeft, MatchResult localResult)
        {
            _issueActive = true;
            int left = UnityEngine.Mathf.CeilToInt((float)secondsLeft);

            if (left <= 0)
            {
                EndMatchByConnection(kind, localResult);
                return;
            }

            if (left != _lastPublishedSecondsLeft)   // раз в секунду, без спама
            {
                _lastPublishedSecondsLeft = left;
                GameEventBus.Publish(new ConnectionIssueUIEvent { Kind = kind, SecondsLeft = left });
                UnityEngine.Debug.LogWarning($"[NetWatch] {kind}: до технического завершения {left}с");
            }
        }

        // Штатный путь конца матча (тот же блок, что GameOverCheckSystem): поп-ап результата покажется сам.
        void EndMatchByConnection(ConnectionIssueKind kind, MatchResult localResult)
        {
            MatchState.IsOver = true;

            var winners = new List<int>();
            var losers  = new List<int>();
            foreach (var pe in _players.Value)
            {
                ref var p = ref _playerPool.Value.Get(pe);
                bool localWins = localResult == MatchResult.Win;
                if (p.IsLocalPlayer == localWins) winners.Add(p.PlayerId);
                else                              losers.Add(p.PlayerId);
                if (_activePool.Value.Has(pe)) _activePool.Value.Del(pe);
            }

            GameEventBus.Publish(new InputBlockedEvent());
            GameEventBus.Publish(new MatchEndedEvent
            {
                LocalResult     = localResult,
                IsDraw          = false,
                WinnerPlayerIds = winners.ToArray(),
                LoserPlayerIds  = losers.ToArray(),
            });

            UnityEngine.Debug.LogWarning($"[NetWatch] матч закрыт по соединению: {kind} → localResult={localResult}");
        }
    }
}
