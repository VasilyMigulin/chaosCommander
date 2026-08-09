using System.Collections.Generic;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Configs;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Network;
using Game.Core.Photon;
using Game.Core.Shared.Interface;
using MemoryPack;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// SELF-HEAL РЕСИНК (MP, спека Docs/network/resync-spec.md). Триггер — NetDesyncDetectedEvent (чексуммы
    /// границы хода разошлись). Авторитет — АКТИВНЫЙ клиент (симулятор): ПАССИВ затемняет экран
    /// (WorldResyncUIEvent) и просит снэпшот (RPC_RequestWorldResync); актив, дождавшись оседания мира,
    /// собирает WorldSnapshotData (Capture) и шлёт чанками; пассив под фейдом СНОСИТ все карты (Dispose
    /// способностей → зомби-подписки, вьюхи, мапы BattleState) и пересобирает мир из снэпшота (Apply),
    /// после чего сверяет контрольную чексумму (WorldStateHash) с чексуммой актива — встроенный самоконтроль.
    /// Вьюхи борда пересоздаст SpawnCreatureViewSystem (BoardTag+BoardPosition), рука — CardDrawnEvent.
    /// v1-границы поля снэпшота — см. WorldSnapshotData. PvE: _photon == null → система спит.
    /// </summary>
    public sealed class WorldResyncSystem : IEcsInitSystem, IEcsRunSystem, IEcsDestroySystem
    {
        const float WaitSnapshotTimeoutSeconds = 20f;
        const float ResyncCooldownSeconds = 15f;   // не чаще раза в N секунд — защита от петли «ресинк каждый ход»
        float _lastResyncAt = -999f;

        readonly EcsWorldInject _world = default;
        readonly EcsCustomInject<PhotonRunHandler> _photon = default;
        readonly EcsCustomInject<CardConfig> _cardConfig = default;
        readonly EcsSharedInject<IGameStateContext> _state = default;

        // «Мир осел» — и для capture (актив), и для apply (пассив): не снимать/собирать состояние посреди каскада.
        readonly EcsFilterInject<Inc<TakeDamageEvent>>        _takeDamage    = default;
        readonly EcsFilterInject<Inc<AttackHitEvent>>         _attackHit     = default;
        readonly EcsFilterInject<Inc<MovingTag>>              _moving        = default;
        readonly EcsFilterInject<Inc<AttackAnimPendingTag>>   _attackAnim    = default;
        readonly EcsFilterInject<Inc<AbilityCastEvent>>       _abilityCast   = default;
        readonly EcsFilterInject<Inc<AbilityTargetingState>>  _abilityTarget = default;
        readonly EcsFilterInject<Inc<AbilityQueuedState>>     _abilityQueued = default;
        readonly EcsFilterInject<Inc<PendingOnCastComponent>> _pendingOnCast = default;
        readonly EcsFilterInject<Inc<DeadTag, BoardTag>>      _deadOnBoard   = default;

        readonly EcsFilterInject<Inc<PlayerComponent>> _players = default;
        readonly EcsFilterInject<Inc<CardModelComponent>> _cards = default;

        int    _currentTurn;
        bool   _waitingSnapshot;      // пассив: запросили, ждём байты
        float  _waitStartedAt;
        bool   _capturePending;       // актив: пришёл запрос, ждём оседания для Capture
        byte[] _pendingApply;         // пассив: байты пришли, ждём оседания для Apply

        const int MaxReconnectRetries = 2;   // повторных запросов снэпшота при восстановлении
        int _reconnectRetries;

        // Страховка ОСТАВШЕГОСЯ: ввод заморожен с момента заявки пира о возвращении. Если тот так и не
        // дошёл до запроса снэпшота (упал на джойне/передумал), разморозить некому — держим свой таймер.
        const float OpponentRestoreLimitSeconds = 30f;
        float _opponentRestoreStartedAt = -1f;

        public void Init(IEcsSystems systems)
        {
            ResyncBus.ClearForBattle();
            GameEventBus.Subscribe<NetDesyncDetectedEvent>(OnDesync);
            GameEventBus.Subscribe<TurnStartedEvent>(OnTurnStarted);
        }

        public void Destroy(IEcsSystems systems)
        {
            GameEventBus.Unsubscribe<NetDesyncDetectedEvent>(OnDesync);
            GameEventBus.Unsubscribe<TurnStartedEvent>(OnTurnStarted);
        }

        void OnTurnStarted(TurnStartedEvent e) { if (e.TurnNumber > _currentTurn) _currentTurn = e.TurnNumber; }

        void OnDesync(NetDesyncDetectedEvent e)
        {
            if (_photon.Value == null || MatchState.IsOver) return;

            if (TurnGate.IsLocalActive(_world.Value))
            {
                // Мы авторитет — ждём RPC-запроса от пассива (он затемнится и попросит снэпшот).
                UnityEngine.Debug.LogWarning($"[Resync] десинк t{e.BoundaryTurn}: мы актив — ждём запрос снэпшота от пира");
                return;
            }

            if (_waitingSnapshot) return;   // уже в процессе
            if (UnityEngine.Time.unscaledTime - _lastResyncAt < ResyncCooldownSeconds)
            {
                UnityEngine.Debug.LogWarning($"[Resync] десинк t{e.BoundaryTurn}: кулдаун ресинка — пропускаем (сойдётся на следующей границе или повторим позже)");
                return;
            }
            _lastResyncAt = UnityEngine.Time.unscaledTime;
            _waitingSnapshot = true;
            _waitStartedAt = UnityEngine.Time.unscaledTime;
            GameEventBus.Publish(new WorldResyncUIEvent { Show = true });
            _photon.Value.RequestWorldResync();
            UnityEngine.Debug.LogWarning($"[Resync] десинк t{e.BoundaryTurn}: запросили снэпшот мира у актива");
        }

        public void Run(IEcsSystems systems)
        {
            if (_photon.Value == null) return;

            // ── РЕКОННЕКТ: мы только что вернулись в идущий бой — мир пуст, просим снэпшот сразу ──
            // (обычный ресинк стартует от расхождения чексумм, а здесь сверять ещё нечего).
            if (ReconnectFlow.IsActive && !_waitingSnapshot && _pendingApply == null)
            {
                _waitingSnapshot = true;
                _waitStartedAt = UnityEngine.Time.unscaledTime;
                GameEventBus.Publish(new WorldResyncUIEvent { Show = true });
                GameEventBus.Publish(new InputBlockedEvent());
                _photon.Value.RequestWorldResync();
                UnityEngine.Debug.LogWarning("[Reconnect] мир пуст — запросил полный снэпшот у пира");
            }

            // ── АКТИВ: пассив попросил снэпшот ─────────────────────────────────────
            if (ResyncBus.TryConsumeSnapshotRequest()) _capturePending = true;
            if (_capturePending && WorldSettled())
            {
                _capturePending = false;
                var snap  = Capture();
                var bytes = MemoryPackSerializer.Serialize(snap);
                _photon.Value.SendWorldSnapshotChunked(bytes);
                UnityEngine.Debug.LogWarning($"[Resync] снэпшот собран и отправлен: cards={snap.Cards.Length} bytes={bytes.Length} hash={snap.StateHash:X16}");

                // Реконнект пира: ввод был заморожен на время сборки (RPC_RequestReconnect) — отпускаем.
                if (ReconnectFlow.OpponentRestoring) ReleaseOpponentRestoreHold("снэпшот отправлен вернувшемуся");
            }

            // Страховка: пир заявился, но снэпшот у нас так и не попросили — не держать ввод вечно.
            if (ReconnectFlow.OpponentRestoring)
            {
                if (_opponentRestoreStartedAt < 0f) _opponentRestoreStartedAt = UnityEngine.Time.unscaledTime;
                else if (UnityEngine.Time.unscaledTime - _opponentRestoreStartedAt > OpponentRestoreLimitSeconds)
                    ReleaseOpponentRestoreHold("вернувшийся не запросил снэпшот за отведённое время");
            }

            // ── ПАССИВ: снэпшот пришёл ─────────────────────────────────────────────
            if (ResyncBus.TryTakeSnapshot(out var data)) _pendingApply = data;
            if (_pendingApply != null && WorldSettled())
            {
                var bytes = _pendingApply;
                _pendingApply = null;
                _waitingSnapshot = false;
                bool wasReconnect = ReconnectFlow.IsActive;
                bool ok = Apply(bytes);
                GameEventBus.Publish(new WorldResyncUIEvent { Show = false });
                UnityEngine.Debug.LogWarning($"[Resync] применение снэпшота завершено: {(ok ? "ЧЕКСУММА СОШЛАСЬ" : "чексумма НЕ сошлась — см. лог")}");

                // Реконнект завершён: дальше матч живёт обычной жизнью (снэпшоты действий, чексуммы,
                // и если что-то всё же разъехалось — обычный self-heal ресинк на ближайшей границе хода).
                if (wasReconnect)
                {
                    ReconnectFlow.Clear();
                    GameEventBus.Publish(new InputRestoredEvent());
                    UnityEngine.Debug.LogWarning($"[Reconnect] ВОССТАНОВЛЕНИЕ ЗАВЕРШЕНО (карт={_cards.Value.GetEntitiesCount()}, чексумма {(ok ? "сошлась" : "НЕ сошлась")})");
                }
            }

            // ── таймаут ожидания (пир умер — добьёт NetConnectionWatchSystem) ──────
            if (_waitingSnapshot && UnityEngine.Time.unscaledTime - _waitStartedAt > WaitSnapshotTimeoutSeconds)
            {
                _waitingSnapshot = false;
                GameEventBus.Publish(new WorldResyncUIEvent { Show = false });
                UnityEngine.Debug.LogError("[Resync] снэпшот не пришёл за таймаут — ресинк отменён");

                // При РЕКОННЕКТЕ отмена фатальна: мир пустой, играть нечем. Пара повторов (пир мог собирать
                // снэпшот дольше таймаута), потом сдаёмся и уходим в меню — матч пиру достанется по watchdog.
                if (ReconnectFlow.IsActive && ++_reconnectRetries > MaxReconnectRetries)
                {
                    ReconnectFlow.Clear();
                    MatchSessionStore.Clear();
                    UnityEngine.Debug.LogError("[Reconnect] снэпшот так и не пришёл — выхожу в меню");
                    GameEventBus.Publish(new ExitToMenuRequestedEvent());
                }
            }
        }

        // Снять заморозку ввода, введённую на время восстановления пира (успех или сдача по таймеру).
        void ReleaseOpponentRestoreHold(string reason)
        {
            ReconnectFlow.ClearOpponentRestoring();
            _opponentRestoreStartedAt = -1f;
            GameEventBus.Publish(new InputRestoredEvent());
            UnityEngine.Debug.LogWarning($"[Reconnect] ввод разблокирован: {reason}");
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

        // ═══════════════════════════ CAPTURE (актив) ═══════════════════════════

        WorldSnapshotData Capture()
        {
            var world = _world.Value;
            var netPool    = world.GetPool<NetworkEntityComponent>();
            var modelPool  = world.GetPool<CardModelComponent>();
            var ownerPool  = world.GetPool<OwnerComponent>();
            var playerPool = world.GetPool<PlayerComponent>();

            var cards = new List<CardSnapshot>();
            foreach (var e in _cards.Value)
            {
                if (!netPool.Has(e)) continue;   // без ключа синхронизировать нечем (MP: у всех карт есть)
                ref var m = ref modelPool.Get(e);

                var cs = new CardSnapshot
                {
                    Key         = netPool.Get(e).NetworkEntityKey,
                    ExpansionId = m.ExpansionId,
                    ModelId     = m.ModelId,
                    OwnerId     = ownerPool.Has(e) ? ownerPool.Get(e).OwnerId : -1,
                    IsCommander = world.GetPool<CommanderTag>().Has(e),
                    Zone        = ZoneOf(world, e),
                };

                if (cs.Zone == 2 && world.GetPool<BoardPositionComponent>().Has(e))
                {
                    ref var pos = ref world.GetPool<BoardPositionComponent>().Get(e);
                    cs.Row = pos.Row; cs.Col = pos.Col; cs.PosOwnerId = pos.OwnerId;
                }

                var atkPool = world.GetPool<AttackComponent>();
                if (atkPool.Has(e))
                {
                    ref var a = ref atkPool.Get(e);
                    cs.HasAttack = true; cs.AtkBase = a.Base;
                    cs.AtkMods = ToArray(a.Modifiers); cs.AtkModsPerm = ToArray(a.ModifiersPermanent);
                }
                var hpPool = world.GetPool<HealthComponent>();
                if (hpPool.Has(e))
                {
                    ref var h = ref hpPool.Get(e);
                    cs.HasHp = true; cs.HpCurrent = h.Current; cs.HpBaseMax = h.BaseMax;
                    cs.HpMods = ToArray(h.Modifiers); cs.HpModsPerm = ToArray(h.ModifiersPermanent);
                }
                var spPool = world.GetPool<SpeedComponent>();
                if (spPool.Has(e))
                {
                    ref var s = ref spPool.Get(e);
                    cs.HasSpeed = true; cs.SpeedBaseMax = s.BaseMax; cs.SpeedRemaining = s.Remaining;
                    cs.SpeedMods = ToArray(s.Modifiers); cs.SpeedModsPerm = ToArray(s.ModifiersPermanent);
                }

                var gold = world.GetPool<GoldCostComponent>();
                var mana = world.GetPool<ManaCostComponent>();
                var hpc  = world.GetPool<HealthCostComponent>();
                if (gold.Has(e))      { ref var c = ref gold.Get(e); cs.CostType = 1; cs.CostBase = c.Base; cs.CostMods = ToArray(c.Modifiers); cs.CostModsPerm = ToArray(c.ModifiersPermanent); }
                else if (mana.Has(e)) { ref var c = ref mana.Get(e); cs.CostType = 2; cs.CostBase = c.Base; cs.CostMods = ToArray(c.Modifiers); cs.CostModsPerm = ToArray(c.ModifiersPermanent); }
                else if (hpc.Has(e))  { ref var c = ref hpc.Get(e);  cs.CostType = 3; cs.CostBase = c.Base; cs.CostMods = ToArray(c.Modifiers); cs.CostModsPerm = ToArray(c.ModifiersPermanent); }

                var auPool = world.GetPool<AttacksUsedComponent>();
                if (auPool.Has(e)) { cs.HasAttacksUsed = true; cs.AttacksUsed = auPool.Get(e).Value; }
                var chPool = world.GetPool<CharmTimerComponent>();
                if (chPool.Has(e)) { cs.HasCharmTimer = true; cs.CharmTurns = chPool.Get(e).TurnsRemaining; }
                var ctPool = world.GetPool<CreatureTimerComponent>();
                if (ctPool.Has(e)) { cs.HasCreatureTimer = true; cs.CreatureTurns = ctPool.Get(e).TurnsRemaining; }
                var daPool = world.GetPool<DurationAuraComponent>();
                if (daPool.Has(e)) { cs.HasAuraTimer = true; cs.AuraTurns = daPool.Get(e).TurnsRemaining; }
                var cdPool = world.GetPool<CommanderCooldownComponent>();
                if (cdPool.Has(e)) { cs.HasCommanderCd = true; cs.CommanderCd = cdPool.Get(e).TurnsRemaining; }
                var htPool = world.GetPool<CommanderHandTracker>();
                if (htPool.Has(e)) { cs.HasCommanderTracker = true; cs.CommanderWasInHand = htPool.Get(e).WasInHand; }
                var gcPool = world.GetPool<GeneratedCardCounterComponent>();
                if (gcPool.Has(e)) { cs.HasGenCounter = true; cs.GenCounterNext = gcPool.Get(e).Next; }
                var pcPool = world.GetPool<BuffPerCharmComponent>();
                if (pcPool.Has(e))
                {
                    ref var pc = ref pcPool.Get(e);
                    cs.HasBuffPerCharm = true; cs.PerCharmAppliedAtk = pc.AppliedAttack; cs.PerCharmAppliedHp = pc.AppliedHealth;
                }

                var abPool = world.GetPool<AppliedBuffsComponent>();
                if (abPool.Has(e))
                {
                    var recs = abPool.Get(e).Records;
                    if (recs != null && recs.Count > 0)
                    {
                        var list = new List<BuffRecordSnapshot>(recs.Count);
                        foreach (var r in recs)
                            if (netPool.Has(r.Target))   // цель без ключа/умерла — реверт по ней уже не нужен
                                list.Add(new BuffRecordSnapshot { TargetKey = netPool.Get(r.Target).NetworkEntityKey, Atk = r.Atk, Hp = r.Hp, Speed = r.Speed });
                        cs.AppliedBuffs = list.ToArray();
                    }
                }

                cards.Add(cs);
            }

            var players = new List<PlayerSnapshot>();
            foreach (var pe in _players.Value)
            {
                ref var p = ref playerPool.Get(pe);
                var ps = new PlayerSnapshot { PlayerId = p.PlayerId };

                var hpPool = world.GetPool<HealthComponent>();
                if (hpPool.Has(pe))
                {
                    ref var h = ref hpPool.Get(pe);
                    ps.Hp = h.Current; ps.HpBaseMax = h.BaseMax;
                    ps.HpMods = ToArray(h.Modifiers); ps.HpModsPerm = ToArray(h.ModifiersPermanent);
                }
                var goldPool = world.GetPool<GoldComponent>();
                if (goldPool.Has(pe)) { ps.Gold = goldPool.Get(pe).Current; ps.GoldMax = goldPool.Get(pe).Max; }
                var manaPool = world.GetPool<ManaComponent>();
                if (manaPool.Has(pe)) { ps.Mana = manaPool.Get(pe).Current; ps.ManaMax = manaPool.Get(pe).Max; }
                var tcPool = world.GetPool<TurnCounterComponent>();
                if (tcPool.Has(pe)) ps.PersonalTurn = tcPool.Get(pe).Personal;

                ps.HandKeys = ZoneKeys(world, world.GetPool<HandComponent>().Has(pe) ? world.GetPool<HandComponent>().Get(pe).CardEntities : null);
                ps.DeckKeys = ZoneKeys(world, world.GetPool<DeckComponent>().Has(pe) ? world.GetPool<DeckComponent>().Get(pe).CardEntities : null);

                var mcPool = world.GetPool<MatchCounterComponent>();
                if (mcPool.Has(pe))
                {
                    ref var mc = ref mcPool.Get(pe);
                    (ps.PlayedModelIds, ps.PlayedCounts)       = DictToArrays(mc.CountsByModelId);
                    (ps.DrawnModelIds, ps.DrawnCounts)         = DictToArrays(mc.DrawnByModelId);
                    (ps.GeneratedModelIds, ps.GeneratedCounts) = DictToArrays(mc.GeneratedByModelId);
                    (ps.InvokedArchetypes, ps.InvokedCounts)   = DictToArrays(mc.InvokedByArchetype);
                    ps.PlayerDamageTaken        = mc.PlayerDamageTaken;
                    ps.PlayerDamageTakenOwnTurn = mc.PlayerDamageTakenOwnTurn;
                    ps.CreaturesDamageTaken     = mc.CreaturesDamageTaken;
                    ps.SpellsPlayed             = mc.SpellsPlayed;
                    (ps.SpellLogExp, ps.SpellLogIds) = LogToArrays(mc.SpellsPlayedLog);
                    (ps.CharmLogExp, ps.CharmLogIds) = LogToArrays(mc.CharmsPlayedLog);
                }

                var cmPool = world.GetPool<CostModifierComponent>();
                if (cmPool.Has(pe)) { ps.HasCostModifier = true; ps.CostModifier = cmPool.Get(pe).Amount; }
                var mfPool = world.GetPool<ManaFloorComponent>();
                if (mfPool.Has(pe)) { ps.HasManaFloor = true; ps.ManaFloor = mfPool.Get(pe).Floor; }
                var tmPool = world.GetPool<TemporaryManaComponent>();
                if (tmPool.Has(pe)) { ps.HasTempMana = true; ps.TempManaRefund = tmPool.Get(pe).RefundAmount; }
                ps.GoldBlock     = world.GetPool<GoldBlockComponent>().Has(pe);
                ps.ReflectDamage = world.GetPool<ReflectDamageComponent>().Has(pe);
                var drPool = world.GetPool<DrawReplacementComponent>();
                if (drPool.Has(pe)) { ps.HasDrawReplacement = true; ps.DrawReplacementLook = drPool.Get(pe).LookCount; ps.DrawReplacementDestroy = drPool.Get(pe).DestroyChosen; }
                ps.TurnResourcesGranted = world.GetPool<TurnResourcesGrantedTag>().Has(pe);

                players.Add(ps);
            }

            // Кто сейчас ходит: ActiveState есть ровно у активного клиента и только на своей сущности-игроке.
            // Если у нас его нет — ход у пира (для реконнекта это и значит «ход вернувшегося»).
            int turnOwner = -1;
            foreach (var pe in world.Filter<PlayerComponent>().Inc<ActiveState>().End())
            { turnOwner = playerPool.Get(pe).PlayerId; break; }
            if (turnOwner < 0)
                foreach (var pe in world.Filter<PlayerComponent>().Exc<LocalComponent>().End())
                { turnOwner = playerPool.Get(pe).PlayerId; break; }

            return new WorldSnapshotData
            {
                TurnNumber        = _currentTurn,
                ActivePlayerId    = LocalPlayerId(),
                TurnOwnerPlayerId = turnOwner,
                MatchOver         = MatchState.IsOver,
                StateHash         = WorldStateHash.Compute(world),
                Players           = players.ToArray(),
                Cards             = cards.ToArray(),
            };
        }

        // ═══════════════════════════ APPLY (пассив) ═══════════════════════════

        bool Apply(byte[] bytes)
        {
            var world = _world.Value;
            WorldSnapshotData snap;
            try { snap = MemoryPackSerializer.Deserialize<WorldSnapshotData>(bytes); }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"[Resync] снэпшот не десериализован: {ex.Message}");
                return false;
            }

            // 0) Транзиенты: протухшие действия/скрэтчи не должны примениться поверх свежего мира.
            ActionQueue.Clear();
            SummonScratch.Clear();
            GeneratedCardChannel.ClearSent();
            ChainContext.CurrentKilled = 0;
            ChainContext.LastDiscardedCost = 0;

            int localId = LocalPlayerId();
            var playerByPid = new Dictionary<int, int>();
            var playerPool = world.GetPool<PlayerComponent>();
            foreach (var pe in _players.Value) playerByPid[playerPool.Get(pe).PlayerId] = pe;

            // 1) Снос всех карт: Dispose способностей (иначе зомби-подписки на шину со старыми id),
            //    вьюхи, UI-слоты руки, сами сущности.
            var refPool   = world.GetPool<AbilityRefComponent>();
            var contPool  = world.GetPool<AbilityContainerComponent>();
            var viewPool  = world.GetPool<ViewRefComponent>();
            var handTag   = world.GetPool<HandTag>();
            var ownerPool = world.GetPool<OwnerComponent>();

            var oldCards = new List<int>();
            foreach (var e in _cards.Value) oldCards.Add(e);
            foreach (var e in oldCards)
            {
                if (contPool.Has(e))
                {
                    var abilities = contPool.Get(e).AbilityEntities;
                    if (abilities != null)
                        foreach (var ae in abilities)
                        {
                            if (ae < 0) continue;
                            // IAbility.Dispose — отписка триггеров от шины (иначе зомби-подписки со старыми id).
                            if (refPool.Has(ae)) refPool.Get(ae).Ability?.Dispose();
                            world.DelEntity(ae);
                        }
                }
                if (viewPool.Has(e) && viewPool.Get(e).View != null)
                    UnityEngine.Object.Destroy(viewPool.Get(e).View);
                if (handTag.Has(e) && ownerPool.Has(e) && ownerPool.Get(e).OwnerId == localId)
                    GameEventBus.Publish(new CardRemovedFromHandUIEvent { CardEntity = e });
                world.DelEntity(e);
            }

            // 2) Мапы BattleState: карты выкинуть (протухшие packed), игроков сохранить.
            _state.Value.ResetCardEntityMaps();

            // 3) Пересоздание карт по снэпшоту (Init из ассета → поверх точное состояние).
            var byKey = new Dictionary<string, int>(snap.Cards.Length);
            foreach (var cs in snap.Cards)
            {
                var inst = _cardConfig.Value.Get(cs.ExpansionId, cs.ModelId);
                if (inst == null || inst.CardData == null)
                {
                    UnityEngine.Debug.LogError($"[Resync] карта {cs.ExpansionId}/{cs.ModelId} не найдена в CardConfig — пропуск {cs.Key}");
                    continue;
                }
                if (!playerByPid.TryGetValue(cs.OwnerId, out int ownerEntity)) ownerEntity = -1;

                int e = inst.CardData.Init(world, ownerEntity, cs.IsCommander);
                byKey[cs.Key] = e;

                world.GetPool<NetworkEntityComponent>().Add(e).NetworkEntityKey = cs.Key;
                ref var owner = ref ownerPool.Add(e);
                owner.OwnerId = cs.OwnerId; owner.EntityKey = cs.Key;
                if (cs.OwnerId == localId) world.GetPool<OwnCardTag>().Add(e);
                else                       world.GetPool<EnemyCardTag>().Add(e);
                _state.Value.AddEntity(e, localKey: e.ToString(), networkKey: cs.Key);

                // Зона
                switch (cs.Zone)
                {
                    case 0: world.GetPool<HandTag>().Add(e); break;
                    case 1: world.GetPool<DeckTag>().Add(e); break;
                    case 2:
                        world.GetPool<BoardTag>().Add(e);
                        ref var pos = ref world.GetPool<BoardPositionComponent>().Add(e);
                        pos.Row = cs.Row; pos.Col = cs.Col; pos.OwnerId = cs.PosOwnerId;
                        break;
                    case 3: world.GetPool<GraveTag>().Add(e); break;
                    case 4: world.GetPool<LimboTag>().Add(e); break;
                }

                // Статы поверх Init (прямые записи + Recalculate; Current после пересчёта)
                if (cs.HasAttack && world.GetPool<AttackComponent>().Has(e))
                {
                    ref var a = ref world.GetPool<AttackComponent>().Get(e);
                    a.Base = cs.AtkBase; a.Modifiers = ToList(cs.AtkMods); a.ModifiersPermanent = ToList(cs.AtkModsPerm);
                    a.RecalculateValue();
                }
                if (cs.HasHp && world.GetPool<HealthComponent>().Has(e))
                {
                    ref var h = ref world.GetPool<HealthComponent>().Get(e);
                    h.BaseMax = cs.HpBaseMax; h.Modifiers = ToList(cs.HpMods); h.ModifiersPermanent = ToList(cs.HpModsPerm);
                    h.RecalculateValue();
                    h.Current = cs.HpCurrent > h.Max ? h.Max : cs.HpCurrent;
                }
                if (cs.HasSpeed && world.GetPool<SpeedComponent>().Has(e))
                {
                    ref var s = ref world.GetPool<SpeedComponent>().Get(e);
                    s.BaseMax = cs.SpeedBaseMax; s.Modifiers = ToList(cs.SpeedMods); s.ModifiersPermanent = ToList(cs.SpeedModsPerm);
                    s.RecalculateValue();
                    s.Remaining = cs.SpeedRemaining > s.Max ? s.Max : cs.SpeedRemaining;
                }
                if (cs.CostType == 1 && world.GetPool<GoldCostComponent>().Has(e))
                { ref var c = ref world.GetPool<GoldCostComponent>().Get(e); c.Base = cs.CostBase; c.Modifiers = ToList(cs.CostMods); c.ModifiersPermanent = ToList(cs.CostModsPerm); c.RecalculateValue(); }
                else if (cs.CostType == 2 && world.GetPool<ManaCostComponent>().Has(e))
                { ref var c = ref world.GetPool<ManaCostComponent>().Get(e); c.Base = cs.CostBase; c.Modifiers = ToList(cs.CostMods); c.ModifiersPermanent = ToList(cs.CostModsPerm); c.RecalculateValue(); }
                else if (cs.CostType == 3 && world.GetPool<HealthCostComponent>().Has(e))
                { ref var c = ref world.GetPool<HealthCostComponent>().Get(e); c.Base = cs.CostBase; c.Modifiers = ToList(cs.CostMods); c.ModifiersPermanent = ToList(cs.CostModsPerm); c.RecalculateValue(); }

                if (cs.HasAttacksUsed)     GetOrAdd<AttacksUsedComponent>(world, e).Value = cs.AttacksUsed;
                if (cs.HasCharmTimer)      GetOrAdd<CharmTimerComponent>(world, e).TurnsRemaining = cs.CharmTurns;
                if (cs.HasCreatureTimer)   GetOrAdd<CreatureTimerComponent>(world, e).TurnsRemaining = cs.CreatureTurns;
                if (cs.HasAuraTimer)       GetOrAdd<DurationAuraComponent>(world, e).TurnsRemaining = cs.AuraTurns;
                if (cs.HasCommanderCd)     GetOrAdd<CommanderCooldownComponent>(world, e).TurnsRemaining = cs.CommanderCd;
                if (cs.HasCommanderTracker) GetOrAdd<CommanderHandTracker>(world, e).WasInHand = cs.CommanderWasInHand;
                if (cs.HasGenCounter)      GetOrAdd<GeneratedCardCounterComponent>(world, e).Next = cs.GenCounterNext;
                if (cs.HasBuffPerCharm && world.GetPool<BuffPerCharmComponent>().Has(e))
                {
                    ref var pc = ref world.GetPool<BuffPerCharmComponent>().Get(e);
                    pc.AppliedAttack = cs.PerCharmAppliedAtk; pc.AppliedHealth = cs.PerCharmAppliedHp;
                }
            }

            // 4) Второй проход: реверт-листы аур (нужен маппинг ключ→новая сущность).
            foreach (var cs in snap.Cards)
            {
                if (cs.AppliedBuffs == null || cs.AppliedBuffs.Length == 0) continue;
                if (!byKey.TryGetValue(cs.Key, out int e)) continue;
                ref var ab = ref GetOrAdd<AppliedBuffsComponent>(world, e);
                ab.Records ??= new List<BuffRecord>();
                ab.Records.Clear();
                foreach (var r in cs.AppliedBuffs)
                    if (byKey.TryGetValue(r.TargetKey, out int target))
                        ab.Records.Add(new BuffRecord { Target = target, Atk = r.Atk, Hp = r.Hp, Speed = r.Speed });
            }

            // 5) Игроки: ресурсы/HP/зоны/счётчики/маркеры.
            foreach (var ps in snap.Players)
            {
                if (!playerByPid.TryGetValue(ps.PlayerId, out int pe)) continue;
                bool isLocal = ps.PlayerId == localId;
                // Ресурсы/личный счётчик ходов АКТИВ зеркалит только для СЕБЯ (RunTurnStartSystem крутится
                // только у активного) — для нас его данные о НАШИХ золоте/мане протухшие. Применяем их только
                // для игрока-отправителя; свои оставляем локальные (боевой баг: «сбросились все ресурсы»).
                bool senderAuthoritative = ps.PlayerId == snap.ActivePlayerId;

                if (world.GetPool<HealthComponent>().Has(pe))
                {
                    ref var h = ref world.GetPool<HealthComponent>().Get(pe);
                    h.BaseMax = ps.HpBaseMax; h.Modifiers = ToList(ps.HpMods); h.ModifiersPermanent = ToList(ps.HpModsPerm);
                    h.RecalculateValue();
                    h.Current = ps.Hp > h.Max ? h.Max : ps.Hp;
                }
                if (senderAuthoritative && world.GetPool<GoldComponent>().Has(pe))
                {
                    ref var g = ref world.GetPool<GoldComponent>().Get(pe);
                    g.Current = ps.Gold; g.Max = ps.GoldMax;
                    GameEventBus.Publish(new ResourceChangedEvent { isLocalPlayer = isLocal, Type = Service.EnumService.ResourceType.Gold, NewValue = g.Current, MaxValue = g.Max });
                }
                if (senderAuthoritative && world.GetPool<ManaComponent>().Has(pe))
                {
                    ref var mn = ref world.GetPool<ManaComponent>().Get(pe);
                    mn.Current = ps.Mana; mn.Max = ps.ManaMax;
                    GameEventBus.Publish(new ResourceChangedEvent { isLocalPlayer = isLocal, Type = Service.EnumService.ResourceType.Mana, NewValue = mn.Current, MaxValue = mn.Max });
                }
                if (senderAuthoritative) GetOrAdd<TurnCounterComponent>(world, pe).Personal = ps.PersonalTurn;

                // Зоны по порядку снэпшота
                ref var hand = ref GetOrAdd<HandComponent>(world, pe);
                hand.CardEntities = ResolveKeys(ps.HandKeys, byKey);
                hand.Count = hand.CardEntities.Count;
                ref var deck = ref GetOrAdd<DeckComponent>(world, pe);
                deck.CardEntities = ResolveKeys(ps.DeckKeys, byKey);
                deck.Count = deck.CardEntities.Count;

                ref var mc = ref GetOrAdd<MatchCounterComponent>(world, pe);
                mc.CountsByModelId    = ArraysToDict(ps.PlayedModelIds, ps.PlayedCounts);
                mc.DrawnByModelId     = ArraysToDict(ps.DrawnModelIds, ps.DrawnCounts);
                mc.GeneratedByModelId = ArraysToDict(ps.GeneratedModelIds, ps.GeneratedCounts);
                mc.InvokedByArchetype = ArraysToDict(ps.InvokedArchetypes, ps.InvokedCounts);
                mc.PlayerDamageTaken        = ps.PlayerDamageTaken;
                mc.PlayerDamageTakenOwnTurn = ps.PlayerDamageTakenOwnTurn;
                mc.CreaturesDamageTaken     = ps.CreaturesDamageTaken;
                mc.SpellsPlayed             = ps.SpellsPlayed;
                mc.SpellsPlayedLog = ArraysToLog(ps.SpellLogExp, ps.SpellLogIds);
                mc.CharmsPlayedLog = ArraysToLog(ps.CharmLogExp, ps.CharmLogIds);

                SetMarker<CostModifierComponent>(world, pe, ps.HasCostModifier, (ref CostModifierComponent c) => c.Amount = ps.CostModifier);
                SetMarker<ManaFloorComponent>(world, pe, ps.HasManaFloor, (ref ManaFloorComponent c) => c.Floor = ps.ManaFloor);
                SetMarker<TemporaryManaComponent>(world, pe, ps.HasTempMana, (ref TemporaryManaComponent c) => c.RefundAmount = ps.TempManaRefund);
                SetMarker<GoldBlockComponent>(world, pe, ps.GoldBlock, null);
                SetMarker<ReflectDamageComponent>(world, pe, ps.ReflectDamage, null);
                SetMarker<DrawReplacementComponent>(world, pe, ps.HasDrawReplacement, (ref DrawReplacementComponent c) => { c.LookCount = ps.DrawReplacementLook; c.DestroyChosen = ps.DrawReplacementDestroy; });
                SetMarker<TurnResourcesGrantedTag>(world, pe, ps.TurnResourcesGranted, null);

                // UI руки: раздать локальную руку заново (борд пересоздаст SpawnCreatureViewSystem).
                if (isLocal)
                    foreach (var c in hand.CardEntities)
                        GameEventBus.Publish(new CardDrawnEvent { CardEntity = c, PlayerId = pe });
            }

            MatchState.IsOver = snap.MatchOver;

            // 5b) РЕКОННЕКТ: вернуть владение ходом. Пока нас не было, пир мог передать ход нам — но ActiveState
            // на нашей стороне поставить некому: его вешает RunActivateSystem по завершении каскада начала хода,
            // а каскад прошёл в прошлой жизни клиента. Ставим маркер НАПРЯМУЮ, без TurnFlow.GrantTurn: тот
            // прокрутил бы каскад заново — второй раз начислил бы доход и добрал карту.
            if (ReconnectFlow.IsActive && snap.TurnOwnerPlayerId == localId
                && playerByPid.TryGetValue(localId, out int meEntity))
            {
                var activePool = world.GetPool<ActiveState>();
                if (!activePool.Has(meEntity)) activePool.Add(meEntity);
                ref var act = ref activePool.Get(meEntity);
                act.TurnNumber = snap.TurnNumber;
                var counterPool = world.GetPool<TurnCounterComponent>();
                act.PersonalTurnNumber = counterPool.Has(meEntity) ? counterPool.Get(meEntity).Personal : 0;
                act.TimeRemaining = TurnConfig.TurnDuration;
                UnityEngine.Debug.LogWarning($"[Reconnect] ход был наш — восстановил ActiveState (turn={snap.TurnNumber})");
            }

            // 6) Контрольная сверка: мир после пересборки обязан дать чексумму актива.
            ulong local = WorldStateHash.Compute(world);
            if (local != snap.StateHash)
                UnityEngine.Debug.LogError($"[Resync] контрольная чексумма НЕ сошлась: local={local:X16} remote={snap.StateHash:X16} — снэпшоту не хватает полей (см. resync-spec v2)");
            return local == snap.StateHash;
        }

        // ── helpers ──────────────────────────────────────────────────────────────

        delegate void RefAction<T>(ref T value) where T : struct;

        static void SetMarker<T>(EcsWorld world, int e, bool present, RefAction<T> fill) where T : struct
        {
            var pool = world.GetPool<T>();
            if (present)
            {
                if (!pool.Has(e)) pool.Add(e);
                if (fill != null) fill(ref pool.Get(e));
            }
            else if (pool.Has(e)) pool.Del(e);
        }

        static ref T GetOrAdd<T>(EcsWorld world, int e) where T : struct
        {
            var pool = world.GetPool<T>();
            if (!pool.Has(e)) pool.Add(e);
            return ref pool.Get(e);
        }

        static byte ZoneOf(EcsWorld world, int e)
        {
            if (world.GetPool<HandTag>().Has(e))  return 0;
            if (world.GetPool<DeckTag>().Has(e))  return 1;
            if (world.GetPool<BoardTag>().Has(e)) return 2;
            if (world.GetPool<GraveTag>().Has(e)) return 3;
            return 4;   // Limbo / вне зон
        }

        static string[] ZoneKeys(EcsWorld world, List<int> entities)
        {
            if (entities == null) return System.Array.Empty<string>();
            var netPool = world.GetPool<NetworkEntityComponent>();
            var keys = new List<string>(entities.Count);
            foreach (var e in entities)
                if (netPool.Has(e)) keys.Add(netPool.Get(e).NetworkEntityKey);
            return keys.ToArray();
        }

        static List<int> ResolveKeys(string[] keys, Dictionary<string, int> byKey)
        {
            var list = new List<int>(keys?.Length ?? 0);
            if (keys != null)
                foreach (var k in keys)
                    if (byKey.TryGetValue(k, out int e)) list.Add(e);
                    else UnityEngine.Debug.LogWarning($"[Resync] ключ зоны не разрезолвлен: {k}");
            return list;
        }

        int LocalPlayerId()
        {
            var world = _world.Value;
            var playerPool = world.GetPool<PlayerComponent>();
            foreach (var pe in world.Filter<PlayerComponent>().Inc<LocalComponent>().End())
                return playerPool.Get(pe).PlayerId;
            return -1;
        }

        static int[] ToArray(List<int> list) => list != null ? list.ToArray() : System.Array.Empty<int>();
        static List<int> ToList(int[] arr) => arr != null ? new List<int>(arr) : new List<int>();

        static (int[], int[]) DictToArrays(Dictionary<int, int> d)
        {
            if (d == null || d.Count == 0) return (System.Array.Empty<int>(), System.Array.Empty<int>());
            var k = new int[d.Count]; var v = new int[d.Count]; int i = 0;
            foreach (var kv in d) { k[i] = kv.Key; v[i] = kv.Value; i++; }
            return (k, v);
        }

        static (string[], int[]) DictToArrays(Dictionary<string, int> d)
        {
            if (d == null || d.Count == 0) return (System.Array.Empty<string>(), System.Array.Empty<int>());
            var k = new string[d.Count]; var v = new int[d.Count]; int i = 0;
            foreach (var kv in d) { k[i] = kv.Key; v[i] = kv.Value; i++; }
            return (k, v);
        }

        static Dictionary<int, int> ArraysToDict(int[] keys, int[] values)
        {
            var d = new Dictionary<int, int>();
            if (keys != null && values != null)
                for (int i = 0; i < keys.Length && i < values.Length; i++) d[keys[i]] = values[i];
            return d;
        }

        static Dictionary<string, int> ArraysToDict(string[] keys, int[] values)
        {
            var d = new Dictionary<string, int>();
            if (keys != null && values != null)
                for (int i = 0; i < keys.Length && i < values.Length; i++) d[keys[i]] = values[i];
            return d;
        }

        static (string[], int[]) LogToArrays(List<SpellPlayRecord> log)
        {
            if (log == null || log.Count == 0) return (System.Array.Empty<string>(), System.Array.Empty<int>());
            var exp = new string[log.Count]; var ids = new int[log.Count];
            for (int i = 0; i < log.Count; i++) { exp[i] = log[i].ExpansionId; ids[i] = log[i].ModelId; }
            return (exp, ids);
        }

        static List<SpellPlayRecord> ArraysToLog(string[] exp, int[] ids)
        {
            var log = new List<SpellPlayRecord>();
            if (exp != null && ids != null)
                for (int i = 0; i < exp.Length && i < ids.Length; i++)
                    log.Add(new SpellPlayRecord { ExpansionId = exp[i], ModelId = ids[i] });
            return log;
        }
    }
}
