using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Конец хода активного (локального) игрока — детерминированно, без таймеров.
    ///   Шаг A (по запросу, СРАЗУ): снять ActiveState (ввод off мгновенно), поднять OnTurnEnd-способности,
    ///       войти в EndTurnState. «Симулятор» (TurnGate.IsLocalActive) держится через EndTurnState,
    ///       поэтому OnTurnEnd срабатывают и реплей не включается раньше времени.
    ///   Шаг B: когда ОСЯДЕТ ability-пайплайн (cast/targeting/queued) — отправить снапшот EndTurn и снять
    ///       EndTurnState (→ игрок становится пассивным, включается реплей).
    /// Анимации НЕ ждём: снапшоты действий шлются в момент НАЧАЛА действия, а не по концу анимации,
    /// поэтому порядок снапшотов уже корректен; ability-пайплайн дренируется детерминированно → таймаут не нужен.
    /// ВАЖНО: регистрировать ПОСЛЕ ability-систем, чтобы видеть актуальное состояние пайплайна.
    /// </summary>
    public sealed class EndTurnRequestSystem : IEcsRunSystem
    {
        readonly EcsPoolInject<EndTurnRequestEvent> _reqPool   = default;
        readonly EcsPoolInject<EndTurnState>        _endPool   = default;
        readonly EcsPoolInject<ActiveState>         _activePool = default;
        readonly EcsPoolInject<OwnerComponent>      _ownerPool = default;
        readonly EcsPoolInject<PlayerComponent>     _playerPool = default;
        readonly EcsPoolInject<TurnEndEvent>        _turnEndEventPool = default;

        readonly EcsFilterInject<Inc<EndTurnRequestEvent, ActiveState, PlayerComponent>> _reqFilter = default;
        readonly EcsFilterInject<Inc<EndTurnState, PlayerComponent>>                     _endFilter = default;
        // Соло: нет сетевого оппонента (RemoteComponent) → передавать ход некому, возвращаем его себе.
        readonly EcsFilterInject<Inc<PlayerComponent, RemoteComponent>> _remotePlayers = default;
        // PvE: ИИ-игрок (без Remote) завершает ход ЭТОЙ ЖЕ системой; ход чередуется человек↔ИИ.
        readonly EcsPoolInject<AiPlayerComponent> _aiPool = default;
        readonly EcsFilterInject<Inc<PlayerComponent>> _allPlayers = default;
        readonly EcsPoolInject<TurnCounterComponent> _counterPool = default;
        readonly EcsFilterInject<Inc<AbilityContainerComponent, BoardTag, OwnerComponent>, Exc<HandTag, DeckTag>> _boardCards = default;

        // Ждём только авто-стадии ability-пайплайна (без AbilityTargetPendingState — это ожидание игрока,
        // которого после снятия ActiveState уже не будет; и без анимаций).
        readonly EcsFilterInject<Inc<AbilityCastEvent>>      _abilityCast   = default;
        readonly EcsFilterInject<Inc<AbilityTargetingState>> _abilityTarget = default;
        readonly EcsFilterInject<Inc<AbilityQueuedState>>    _abilityQueued = default;
        // ВАЖНО: ждём и НЕЗАВЕРШЁННЫЙ каст (форс-плей токена с добора: RequestCardCastEvent добавлен, но роутер
        // ещё не сделал из него AbilityCastEvent). Иначе ранний End Turn хендофит ход до сбора ActionCastData
        // авто-каста → у пассива зеркало руки держит фантом разыгранного токена.
        readonly EcsFilterInject<Inc<RequestCardCastEvent>>  _castRequest   = default;
        readonly EcsFilterInject<Inc<CastEvent>>             _castInProgress = default;
        // #2: ждём гейт «призыв → OnCast» — иначе ранний End Turn хендофит ход до отложенного OnCast
        // (напр. SelfDestruct/деатрэттл Всадников) → его снапшот уехал бы после передачи хода.
        readonly EcsFilterInject<Inc<PendingOnCastComponent>> _pendingOnCast = default;
        // Раскопка (DiscoverEffect) ещё не резолвнута — RunDiscoverSystem форсит случайный выбор на
        // TurnEndedEvent (см. ForceResolveForEndedTurn), но это отдельная система/следующий тик; ждём, пока
        // её запись реально уйдёт (без этого гейта Step B хендофил бы ход, пока discover-запрос ещё жив).
        readonly EcsFilterInject<Inc<DiscoverRequestComponent>> _discoverPending = default;
        // Цепочка (RunChainSystem — AbilityChain/RepeatAbility) ещё резолвится (снаряд летит между стадиями
        // ИЛИ мир оседает между ними) — без этого гейта Step B хендофил бы ход до применения её эффектов.
        readonly EcsFilterInject<Inc<ChainStateComponent>> _chainResolving = default;
        // Существо ещё доигрывает ВИЗУАЛЬНУЮ анимацию смерти — DeadTag/BoardTag снимает DieSystem
        // синхронно ДО запуска анимации, поэтому без этого гейта ход мог хендофиться раньше, чем
        // предсмертный эффект (напр. Водонос → Водица в руку) успевал прийти (баг 2026-08-11, PvE).
        readonly EcsFilterInject<Inc<DeathAnimPendingTag>> _deathAnim = default;

        public void Run(IEcsSystems systems)
        {
            if (MatchState.IsOver) return;   // матч окончен — конец хода не обрабатываем

            // Шаг A: запрос → СРАЗУ снять ActiveState, поднять OnTurnEnd, войти в EndTurnState.
            foreach (var entity in _reqFilter.Value)
            {
                // PvE: ИИ-игрок завершает ход тем же путём (RunAiTurnSystem ставит EndTurnRequestEvent).
                if (!_playerPool.Value.Get(entity).IsLocalPlayer && !_aiPool.Value.Has(entity)) continue;

                _reqPool.Value.Del(entity);
                if (_endPool.Value.Has(entity)) continue;   // уже завершаем — игнор повторного запроса

                int playerId = _playerPool.Value.Get(entity).PlayerId;

                foreach (var ce in _boardCards.Value)
                {
                    if (_ownerPool.Value.Get(ce).OwnerId != playerId) continue;
                    if (!_turnEndEventPool.Value.Has(ce)) _turnEndEventPool.Value.Add(ce);
                }

                _endPool.Value.Add(entity).AbilitiesFired = true;
                _activePool.Value.Del(entity);          // ввод off СРАЗУ; «симулятор» держится через EndTurnState
                GameEventBus.Publish(new InputBlockedEvent());

                // OnTurnEnd-триггеры способностей (bus): ловит OnTurnEndTrigger, вешает AbilityCastEvent.
                // Публикуем ПОКА держится EndTurnState (TurnGate.IsLocalActive=true) → AbilityFire.Mark
                // пропустит, и поднятый ability-пайплайн осядет до передачи хода (Шаг B ждёт его).
                GameEventBus.Publish(new TurnEndedEvent { ActivePlayerId = playerId });
                UnityEngine.Debug.Log($"[EndTurn] player {playerId}: ActiveState removed, OnTurnEnd raised, settling abilities…");
                return;
            }

            // Шаг B: дождаться оседания ability-пайплайна → отправить EndTurn-снапшот, снять EndTurnState.
            if (_endFilter.Value.GetEntitiesCount() == 0) { _waitingSince = 0f; return; }
            if (AbilitiesPending()) { ReportIfStuck(); return; }
            _waitingSince = 0f;

            var world = systems.GetWorld();
            bool solo = _remotePlayers.Value.GetEntitiesCount() == 0;   // нет сетевого оппонента

            foreach (var entity in _endFilter.Value)
            {
                if (!_playerPool.Value.Get(entity).IsLocalPlayer && !_aiPool.Value.Has(entity)) continue;

                _endPool.Value.Del(entity);

                // Откат временных множителей частоты владельца (его ход кончился, end-триггеры уже осели).
                // Зеркально на пассиве — ReplayEndTurn. См. SetTriggerMultiplierEffect{Temporary}.
                CastMultiplierService.TickOwnerTurnEnd(_playerPool.Value.Get(entity).PlayerId);

                if (solo)
                {
                    // PvE: оппонент есть, но ЛОКАЛЬНЫЙ (ИИ, без Remote) → передаём ход ДРУГОМУ игроку.
                    // Глобальный номер хода = сумма личных счётчиков обоих + 1 (счётчик выданных ходов).
                    int other = FindOtherPlayer(entity);
                    if (Service.PveMode.Enabled && other >= 0)
                    {
                        TurnFlow.GrantTurn(world, other, TotalTurnsGranted() + 1);
                        bool toAi = _aiPool.Value.Has(other);
                        if (toAi)  GameEventBus.Publish(new OpponentTurnEndedEvent());   // UI: «ход оппонента»
                        UnityEngine.Debug.Log($"[EndTurn] PvE: ход передан {(toAi ? "ИИ" : "игроку")}");
                        return;
                    }

                    // Истинное соло: оппонента нет → не уходим в пассив, сразу возвращаем ход локальному игроку
                    // (новый каскад начала хода: ресурсы/добор/OnTurnStart → ActiveState).
                    int next = _counterPool.Value.Has(entity) ? _counterPool.Value.Get(entity).Personal + 1 : 1;
                    TurnFlow.GrantTurn(world, entity, next);
                    UnityEngine.Debug.Log("[EndTurn] solo: оппонента нет → ход возвращён локальному игроку");
                    return;
                }

                GameEventBus.Publish(new EndTurnNetEvent());         // коллектор пошлёт ActionEndTurnData
                GameEventBus.Publish(new OpponentTurnEndedEvent());  // UI: «ход оппонента»
                UnityEngine.Debug.Log("[EndTurn] handed off (EndTurn snapshot sent)");
                return;
            }
        }

        int FindOtherPlayer(int current)
        {
            foreach (var e in _allPlayers.Value)
                if (e != current) return e;
            return -1;
        }

        int TotalTurnsGranted()
        {
            int total = 0;
            foreach (var e in _allPlayers.Value)
                if (_counterPool.Value.Has(e)) total += _counterPool.Value.Get(e).Personal;
            return total;
        }

        // ── Диагностика зависшей передачи хода ───────────────────────────────
        // Шаг B ждёт БЕЗ ТАЙМАУТА (и правильно: дренаж детерминирован, а «протухший» хендофф хуже
        // задержки). Но если что-то всё-таки не оседает, ход не передаётся НИКОГДА — в логе просто
        // обрывается всё после «settling abilities…», и виновника приходится угадывать (софт-лок
        // 2026-08-03: раскопка, сработавшая вне хода владельца, ждала окно, которое некому показать).
        // Поэтому: подождали дольше разумного — ОДИН РАЗ печатаем, ЧТО именно висит.
        float _waitingSince;
        const float StuckReportAfter = 8f;

        void ReportIfStuck()
        {
            if (_waitingSince == 0f) { _waitingSince = UnityEngine.Time.time; return; }
            if (_waitingSince < 0f) return;                                   // уже отчитались
            if (UnityEngine.Time.time - _waitingSince < StuckReportAfter) return;

            var sb = new System.Text.StringBuilder("[EndTurn] передача хода ЗАСТРЯЛА, не осели:");
            Append(sb, "AbilityCastEvent",        _abilityCast.Value.GetEntitiesCount());
            Append(sb, "AbilityTargetingState",   _abilityTarget.Value.GetEntitiesCount());
            Append(sb, "AbilityQueuedState",      _abilityQueued.Value.GetEntitiesCount());
            Append(sb, "RequestCardCastEvent",    _castRequest.Value.GetEntitiesCount());
            Append(sb, "CastEvent",               _castInProgress.Value.GetEntitiesCount());
            Append(sb, "PendingOnCastComponent",  _pendingOnCast.Value.GetEntitiesCount());
            Append(sb, "DiscoverRequestComponent", _discoverPending.Value.GetEntitiesCount());
            Append(sb, "ChainStateComponent",      _chainResolving.Value.GetEntitiesCount());
            Append(sb, "DeathAnimPendingTag",      _deathAnim.Value.GetEntitiesCount());
            UnityEngine.Debug.LogError(sb.ToString());
            _waitingSince = -1f;
        }

        static void Append(System.Text.StringBuilder sb, string name, int count)
        {
            if (count > 0) sb.Append(' ').Append(name).Append('=').Append(count);
        }

        bool AbilitiesPending()
            => _abilityCast.Value.GetEntitiesCount()    > 0
            || _abilityTarget.Value.GetEntitiesCount()  > 0
            || _abilityQueued.Value.GetEntitiesCount()  > 0
            || _castRequest.Value.GetEntitiesCount()    > 0   // форс-плей: каст запрошен, ещё не разрезолвлен
            || _castInProgress.Value.GetEntitiesCount() > 0   // каст в процессе (до OnCast-способностей)
            || _pendingOnCast.Value.GetEntitiesCount()  > 0   // #2: призыв не «дозрел» до OnCast
            || _discoverPending.Value.GetEntitiesCount() > 0  // раскопка не резолвнута (окно/форс-выбор)
            || _chainResolving.Value.GetEntitiesCount() > 0   // цепочка (RunChainSystem) ещё резолвится
            || _deathAnim.Value.GetEntitiesCount() > 0;       // существо ещё доигрывает анимацию смерти
    }
}
