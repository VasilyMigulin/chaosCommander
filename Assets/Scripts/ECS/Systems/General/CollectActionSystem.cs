using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Network;
using Game.Core.Photon;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using MemoryPack;
using System.Collections.Generic;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Собирает входные действия локального (активного) игрока в типизированные IActionData
    /// и немедленно отправляет каждое оппоненту через PhotonRunHandler.
    ///
    /// Записываются только входные действия (что и как запускаем).
    /// Результаты (урон, смерть, добор и т.д.) воспроизводятся детерминированно на обеих сторонах.
    ///
    /// ActionIndex нумеруется в рамках одного хода и сбрасывается при TurnStartedEvent.
    /// </summary>
    public sealed class CollectActionSystem : IEcsInitSystem, IEcsRunSystem, IEcsDestroySystem, System.IDisposable
    {
        readonly EcsWorldInject _world = default;
        readonly EcsCustomInject<PhotonRunHandler> _photon = default;
        readonly EcsPoolInject<NetworkEntityComponent> _netKeyPool = default;
        readonly EcsPoolInject<OwnCardTag> _ownCardPool = default;
        readonly EcsPoolInject<BoardPositionComponent> _posPool = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsPoolInject<CreatureTag> _creaturePool = default;
        readonly EcsPoolInject<LocalComponent> _localPool = default;   // атакующий-аватар: OwnCardTag на нём не бывает

        private int _currentTurnNumber;
        private int _actionIndex;

        private readonly List<IActionData> _pendingActions = new List<IActionData>();

        // ── Init / Destroy ────────────────────────────────────────────────────

        public void Init(IEcsSystems systems)
        {
            GameEventBus.Subscribe<TurnStartedEvent>(OnTurnStarted);
            GameEventBus.Subscribe<CardCastEvent>(OnCardCast);
            GameEventBus.Subscribe<CreatureInvokedEvent>(OnCreatureInvoked);
            GameEventBus.Subscribe<CreatureMovedEvent>(OnCreatureMoved);
            GameEventBus.Subscribe<CreatureAttackedEvent>(OnCreatureAttacked);
            GameEventBus.Subscribe<CardPickResolvedNetEvent>(OnCardPicked);
            GameEventBus.Subscribe<AbilityResolvedNetEvent>(OnAbilityResolved);
            GameEventBus.Subscribe<TimerDeathNetEvent>(OnTimerDeath);
            GameEventBus.Subscribe<TimerDiscardNetEvent>(OnTimerDiscard);
            GameEventBus.Subscribe<ControlRevertedNetEvent>(OnControlReverted);
            GameEventBus.Subscribe<DrawReplacementResolvedNetEvent>(OnDrawReplacement);
            GameEventBus.Subscribe<DeckDrawNetEvent>(OnDeckDraw);
            GameEventBus.Subscribe<EndTurnNetEvent>(OnEndTurn);
            UnityEngine.Debug.Log("[Collect] init: subscribed");
        }

        // EcsSystems.Destroy() ищет IEcsDestroySystem, не System.IDisposable — без этого моста Dispose()
        // фреймворк никогда не вызывал бы (см. EcsRunHandler/TutorialEcsHandler.Dispose → _allSystems.Destroy()).
        public void Destroy(IEcsSystems systems) => Dispose();

        public void Dispose()
        {
            GameEventBus.Unsubscribe<TurnStartedEvent>(OnTurnStarted);
            GameEventBus.Unsubscribe<CardCastEvent>(OnCardCast);
            GameEventBus.Unsubscribe<CreatureInvokedEvent>(OnCreatureInvoked);
            GameEventBus.Unsubscribe<CreatureMovedEvent>(OnCreatureMoved);
            GameEventBus.Unsubscribe<CreatureAttackedEvent>(OnCreatureAttacked);
            GameEventBus.Unsubscribe<CardPickResolvedNetEvent>(OnCardPicked);
            GameEventBus.Unsubscribe<AbilityResolvedNetEvent>(OnAbilityResolved);
            GameEventBus.Unsubscribe<TimerDeathNetEvent>(OnTimerDeath);
            GameEventBus.Unsubscribe<TimerDiscardNetEvent>(OnTimerDiscard);
            GameEventBus.Unsubscribe<ControlRevertedNetEvent>(OnControlReverted);
            GameEventBus.Unsubscribe<DrawReplacementResolvedNetEvent>(OnDrawReplacement);
            GameEventBus.Unsubscribe<DeckDrawNetEvent>(OnDeckDraw);
            GameEventBus.Unsubscribe<EndTurnNetEvent>(OnEndTurn);
        }

        private void OnEndTurn(EndTurnNetEvent evt)
        {
            Enqueue(new ActionEndTurnData
            {
                TurnNumber  = _currentTurnNumber,
                ActionIndex = _actionIndex++,
            });
        }

        // ── ECS Run ───────────────────────────────────────────────────────────

        public void Run(IEcsSystems systems)
        {
            if (_pendingActions.Count == 0) return;
            if (_photon.Value == null) { _pendingActions.Clear(); return; }

            foreach (var action in _pendingActions)
                SendAction(action);

            _pendingActions.Clear();
        }

        // ── Event handlers ────────────────────────────────────────────────────

        private void OnTurnStarted(TurnStartedEvent evt)
        {
            _currentTurnNumber = evt.TurnNumber;
            _actionIndex = 0;
        }

        private void OnCardCast(CardCastEvent evt)
        {
            // Гейт «я ли симулятор» (как OnAbilityResolved), а НЕ по владельцу карты: актив симулирует и
            // форс-касты на ЗЕРКАЛЕ карт оппонента (Попойка: OnDrawForcePlay, когда добор оппонента случился
            // в НАШ ход). IsOwnCard-гейт такой каст ТЕРЯЛ → у владельца-пассива карта оставалась в руке →
            // расхождение рук/колод (десинки границ 21/23/24 от 2026-07-27). Эха нет: на пассиве
            // CardCastEvent не публикуется вовсе (реплей размещает карты без него).
            if (!TurnGate.IsLocalActive(_world.Value)) return;
            // СУЩЕСТВО синкает размещение через CreatureInvokedEvent (единый путь розыгрыш+призыв);
            // здесь — только заклинания/чары (они CreatureInvokedEvent не публикуют). Иначе у сыгранного
            // существа было бы ДВА ActionCastData (оно публикует и Cast, и Invoked).
            if (_creaturePool.Value.Has(evt.CardEntity)) return;
            EnqueueCast(evt.CardEntity);
        }

        // Существо вышло на стол (розыгрыш ИЛИ призыв) → синкаем размещение. Призыв CardCastEvent не шлёт,
        // поэтому без этого пути призванные существа не появлялись бы у оппонента.
        private void OnCreatureInvoked(CreatureInvokedEvent evt)
        {
            // Токены, созданные сразу на борд (FillRow/SpawnCardOnBoard): создание зеркально через детерм.
            // ре-ран generate-эффекта у обоих — ActionCastData дал бы ДУБЛЬ размещения у пассива.
            if (evt.Generated) return;
            // Гейт «я ли симулятор» (см. OnCardCast): ловит и форс-розыгрыш существа с зеркала оппонента.
            // Заодно убирает латентное эхо: ReplayCast на пассиве публикует CreatureInvokedEvent для
            // трекинга — под старым IsOwnCard-гейтом НАША карта, форс-разыгранная симулятором, эхнулась бы
            // обратно; IsLocalActive на пассиве всегда false → эха нет.
            if (!TurnGate.IsLocalActive(_world.Value)) return;
            EnqueueCast(evt.CardEntity);
        }

        private void EnqueueCast(int cardEntity)
        {
            // Клетка берётся из BoardPosition (к этому моменту существо уже размещено); заклинание/чары — -1.
            int cell = -1;
            if (_posPool.Value.Has(cardEntity))
            {
                ref var pos = ref _posPool.Value.Get(cardEntity);
                cell = pos.Row * 5 + pos.Col;
            }

            // Альтернативная уплата (Букмекер и семейство): роутер оставил на карте результат — передаём
            // пассиву (-1 = обычная оплата ресурсом).
            int altKind = -1, altAmount = 0;
            string[] altKeys = null;
            var altPaidPool = _world.Value.GetPool<AltPaidComponent>();
            if (altPaidPool.Has(cardEntity))
            {
                ref var paid = ref altPaidPool.Get(cardEntity);
                altKind = (int)paid.Kind; altAmount = paid.Amount; altKeys = paid.Keys;
                altPaidPool.Del(cardEntity);
            }

            Enqueue(new ActionCastData
            {
                TurnNumber      = _currentTurnNumber,
                ActionIndex     = _actionIndex++,
                SourceEntityKey = GetNetKey(cardEntity),
                Cell            = cell,
                AltPaidKind     = altKind,
                AltPaidAmount   = altAmount,
                AltPaidKeys     = altKeys,
            });
        }

        // ГЕЙТ-АУДИТ (2026-07-28, после бага «Попойки»): Move/Attack/TimerDeath ОСТАВЛЕНЫ на IsOwnCard —
        // сегодня он эквивалентен «я симулятор» (двигают/атакуют только по инпуту своих существ; таймеры
        // тикают только у владельца в его ход) и БЕЗОПАСНЕЕ при поздних анти-софтлок стрэгглерах (действие,
        // доехавшее после снятия ActiveState, под IsLocalActive потерялось бы молча). Если появится эффект,
        // гоняющий ЧУЖИХ существ через мув/атак-пайплайн (MoveRequestEvent/AttackRequestEvent), — пересмотреть.
        private void OnCreatureMoved(CreatureMovedEvent evt)
        {
            if (!IsOwnCard(evt.CreatureEntity)) return;

            Enqueue(new ActionMoveData
            {
                TurnNumber      = _currentTurnNumber,
                ActionIndex     = _actionIndex++,
                SourceEntityKey = GetNetKey(evt.CreatureEntity),
                TargetCell      = evt.ToRow * 5 + evt.ToCol,
                TargetOwnerId   = evt.ToOwnerId,
            });
        }

        private void OnCreatureAttacked(CreatureAttackedEvent evt)
        {
            // Атакующий — либо своя карта на борде (IsOwnCard), либо СВОЙ аватар (AvatarAttackSystem):
            // у entity игрока никогда не бывает OwnCardTag, поэтому гейт «я симулятор» ведёт LocalComponent
            // — тот же принцип, что и IsOwnCard («двигают/атакуют только по инпуту своих»), просто для игрока.
            bool mine = IsOwnCard(evt.AttackerEntity)
                || (_playerPool.Value.Has(evt.AttackerEntity) && _localPool.Value.Has(evt.AttackerEntity));
            if (!mine) return;

            Enqueue(new ActionAttackData
            {
                TurnNumber          = _currentTurnNumber,
                ActionIndex         = _actionIndex++,
                AttackerEntityKey   = TargetKey(evt.AttackerEntity),   // игрок (аватар) → "PLAYER:{id}"
                DefenderEntityKey   = TargetKey(evt.DefenderEntity),   // игрок → "PLAYER:{id}"
            });
        }

        private void OnCardPicked(CardPickResolvedNetEvent evt)
        {
            // Своя карта ИЛИ «я симулятор». IsOwnCard одного мало: раскопку ЧУЖОЙ карты, сработавшую в наш
            // ход (хрип пиньяты оппонента разыграл его «Приглашение»), решает симулятор — под чистым
            // IsOwnCard-гейтом его выбор не уехал бы владельцу, и у того запрос завис бы навсегда (та же
            // семья, что фикс коллектора по Попойке). IsLocalActive одного тоже мало: форс-резолв раскопки
            // на конце хода может пройти после снятия ActiveState — правда, EndTurnState ещё держится, так
            // что гейт истинен, но оставляем IsOwnCard первым условием как более узкое и надёжное.
            // Эха нет: пассив EmitResolved не публикует (реплей-ветка резолвит молча).
            if (!IsOwnCard(evt.CastingCardEntity) && !TurnGate.IsLocalActive(_world.Value)) return;

            Enqueue(new ActionCardPickedData
            {
                TurnNumber        = _currentTurnNumber,
                ActionIndex       = _actionIndex++,
                SourceEntityKey   = !string.IsNullOrEmpty(evt.CastingCardNetworkKey)
                    ? evt.CastingCardNetworkKey
                    : GetNetKey(evt.CastingCardEntity),
                ChosenEntityKey   = evt.ChosenCardNetworkKey,
                CreateFromPool    = evt.CreateFromPool,
                ChosenExpansionId = evt.ChosenExpansionId,
                ChosenCardId      = evt.ChosenCardId,
            });
        }

        private void OnAbilityResolved(AbilityResolvedNetEvent evt)
        {
            // Собираем ВСЁ, что разрешено на АКТИВНОМ клиенте в этот ход — и свои способности, и РЕАКЦИИ
            // оппонента (его ауры/триггеры на наш ход симулирует активный клиент: триггеры клиент-уровневые,
            // гейтятся IsLocalActive в AbilityFire). Гейт по «я ли симулятор», а НЕ по владельцу карты:
            //   • иначе реакции оппонента на наш ход не уезжали бы к нему → рассинхрон (ауры по врагам и пр.);
            //   • на пассиве IsLocalActive=false → резолв впрыснутой реплеем очереди (в т.ч. НАШЕЙ карты,
            //     присланной нам как реакция) НЕ эхнётся обратно отправителю.
            if (!TurnGate.IsLocalActive(_world.Value))
            {
                UnityEngine.Debug.Log($"[Collect] ability resolve skipped (not simulator): card={evt.SourceCardEntity}");
                return;
            }

            int n = evt.TargetEntities?.Length ?? 0;
            var keys = new string[n];
            for (int i = 0; i < n; i++) keys[i] = TargetKey(evt.TargetEntities[i]);

            int sn = evt.SummonedEntities?.Length ?? 0;
            var summonedKeys = new string[sn];
            for (int i = 0; i < sn; i++) summonedKeys[i] = GetNetKey(evt.SummonedEntities[i]);

            Enqueue(new ActionAbilityData
            {
                TurnNumber            = _currentTurnNumber,
                ActionIndex           = _actionIndex++,
                SourceEntityKey       = GetNetKey(evt.SourceCardEntity),
                AbilityIndex          = evt.AbilityIndex,
                TargetEntityKeys      = keys,
                SummonedEntityKeys    = summonedKeys,
                StepIndex             = evt.StepIndex,
                KilledCount           = evt.KilledCount,
                GeneratedExpansionIds = evt.GeneratedExpansionIds,
                GeneratedCardIds      = evt.GeneratedCardIds,
            });
        }

        private void OnTimerDeath(TimerDeathNetEvent evt)
        {
            if (!IsOwnCard(evt.CreatureEntity)) return;   // шлём смерть только своих существ

            Enqueue(new ActionDeathData
            {
                TurnNumber  = _currentTurnNumber,
                ActionIndex = _actionIndex++,
                EntityKey   = GetNetKey(evt.CreatureEntity),
            });
        }

        private void OnTimerDiscard(TimerDiscardNetEvent evt)
        {
            if (!IsOwnCard(evt.CardEntity)) return;   // шлём сброс только своих карт

            Enqueue(new ActionDiscardData
            {
                TurnNumber  = _currentTurnNumber,
                ActionIndex = _actionIndex++,
                EntityKey   = GetNetKey(evt.CardEntity),
            });
        }

        private void OnControlReverted(ControlRevertedNetEvent evt)
        {
            // Откат уже применён локально (актив-контролёр в конце своего хода). Шлём ВСЕГДА — без IsOwnCard:
            // к этому моменту существо уже возвращено исходному владельцу (OwnCardTag снят), а событие и так
            // фейрит только активный контролёр. Пассив повторит откат по ключу из своей TempControlledComponent.
            Enqueue(new ActionControlRevertData
            {
                TurnNumber  = _currentTurnNumber,
                ActionIndex = _actionIndex++,
                EntityKey   = GetNetKey(evt.CreatureEntity),
            });
        }

        private void OnDrawReplacement(DrawReplacementResolvedNetEvent evt)
        {
            int n = evt.Offered?.Length ?? 0;
            var keys = new string[n];
            for (int i = 0; i < n; i++) keys[i] = GetNetKey(evt.Offered[i]);

            Enqueue(new ActionDrawReplacementData
            {
                TurnNumber    = _currentTurnNumber,
                ActionIndex   = _actionIndex++,
                PlayerId      = _playerPool.Value.Has(evt.PlayerEntity) ? _playerPool.Value.Get(evt.PlayerEntity).PlayerId : -1,
                OfferedKeys   = keys,
                ChosenKey     = GetNetKey(evt.Chosen),
                DestroyChosen = evt.DestroyChosen,
            });
        }

        // Turn-start добор активного игрока → синкаем оппоненту (он повторит снятие верхних карт).
        // Только Sync-доборы (DrawCardSystem), эффектные доборы ре-ранятся резолвом на обоих → сюда не идут.
        private void OnDeckDraw(DeckDrawNetEvent evt)
        {
            int playerId = _playerPool.Value.Has(evt.PlayerEntity) ? _playerPool.Value.Get(evt.PlayerEntity).PlayerId : -1;

            // Ключи добранных карт → пассив перенесёт в руку ИМЕННО их (а не «верхние N»).
            string[] keys = null;
            if (evt.DrawnEntities != null)
            {
                keys = new string[evt.DrawnEntities.Length];
                for (int i = 0; i < keys.Length; i++) keys[i] = GetNetKey(evt.DrawnEntities[i]);
            }

            Enqueue(new ActionDrawData
            {
                TurnNumber  = _currentTurnNumber,
                ActionIndex = _actionIndex++,
                PlayerId    = playerId,
                Count       = evt.Count,
                DrawnKeys   = keys,
            });
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void Enqueue(IActionData action)
        {
            _pendingActions.Add(action);
            UnityEngine.Debug.Log($"[Collect] queued {action.GetType().Name} turn={action.TurnNumber} idx={action.ActionIndex}");
        }

        private void SendAction(IActionData action)
        {
            byte[] data = MemoryPackSerializer.Serialize(action);
            UnityEngine.Debug.Log($"[Collect] SEND {action.GetType().Name} idx={action.ActionIndex} bytes={data.Length}");
            _photon.Value.RPC_SendActionSnapshot(data);
        }

        private bool IsOwnCard(int entity)
        {
            return entity >= 0 && _ownCardPool.Value.Has(entity);
        }

        private string GetNetKey(int entity)
        {
            if (entity < 0) return "";
            if (_netKeyPool.Value.Has(entity))
                return _netKeyPool.Value.Get(entity).NetworkEntityKey;
            return "";
        }

        /// <summary>Ключ цели: сущность → NetworkEntityKey; игрок → "PLAYER:{PlayerId}".</summary>
        private string TargetKey(int entity)
        {
            if (entity < 0) return "";
            if (_netKeyPool.Value.Has(entity))
                return _netKeyPool.Value.Get(entity).NetworkEntityKey;
            if (_playerPool.Value.Has(entity))
                return "PLAYER:" + _playerPool.Value.Get(entity).PlayerId;
            UnityEngine.Debug.LogWarning($"[Collect] TargetKey: no key for entity={entity}");
            return "";
        }
    }
}
