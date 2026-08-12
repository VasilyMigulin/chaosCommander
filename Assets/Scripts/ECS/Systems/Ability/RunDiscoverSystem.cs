using System.Collections.Generic;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Network;
using Game.Core.Shared;
using Game.Core.Configs;
using Game.Core.Instance.Card;
using Game.Core.Shared.Interface;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// Раскопка-ЭФФЕКТ (DiscoverEffect, NonTarget): предложить N карт и положить выбранную В РУКУ.
    /// Запрос (DiscoverRequestComponent) ставит DiscoverEffect.Apply на ОБОИХ клиентах.
    ///   • Своя карта (OwnCardTag) → показать окно (CardPickOfferedEvent), ждать CardPickChosenEvent.
    ///   • Чужая (реплей) → авто-резолв из CardPickReplayStore по NetKey источника (UI нет).
    /// Терминал: зона → перевесить СУЩЕСТВУЮЩУЮ карту в руку; пул → создать НОВУЮ (CreateCardEvent).
    ///
    /// СИНК — через готовый канал: на активе эмитим CardPickResolvedNetEvent → CollectActionSystem
    /// пакует ActionCardPickedData → пассив ReplayActionSystem → CardPickReplayStore (как у каст-пика).
    /// Для пула общий ключ — Guid (передаётся); для зоны — NetworkEntityKey существующей карты.
    /// </summary>
    public sealed class RunDiscoverSystem : IEcsInitSystem, IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsSharedInject<IGameStateContext> _state = default;
        readonly EcsCustomInject<CardConfig> _cardConfig = default;

        readonly EcsFilterInject<Inc<DiscoverRequestComponent>> _reqFilter = default;
        readonly EcsPoolInject<DiscoverRequestComponent> _reqPool = default;

        readonly EcsPoolInject<OwnCardTag>             _ownPool    = default;
        readonly EcsPoolInject<ActiveState>            _activePool = default;
        readonly EcsPoolInject<NetworkEntityComponent> _netPool    = default;
        readonly EcsPoolInject<CardModelComponent>     _modelPool  = default;

        readonly EcsPoolInject<DeckComponent> _deckPool     = default;
        readonly EcsPoolInject<HandComponent> _handPool     = default;
        readonly EcsPoolInject<DeckTag>       _deckTagPool  = default;
        readonly EcsPoolInject<HandTag>       _handTagPool  = default;
        readonly EcsPoolInject<GraveTag>      _graveTagPool = default;
        readonly EcsPoolInject<SideboardTag>  _sideboardTagPool = default;   // «отложенные» (Сказочник)

        // Терминал Dest+TakeOwnership (воровство/замешивание/сброс).
        readonly EcsPoolInject<OwnerComponent>  _ownerPool   = default;
        readonly EcsPoolInject<OwnCardTag>      _ownTagPool  = default;
        readonly EcsPoolInject<EnemyCardTag>    _enemyTagPool = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool  = default;
        readonly EcsFilterInject<Inc<PlayerComponent>> _playerFilter = default;

        bool _subscribed;
        readonly Queue<CardPickChosenEvent>    _chosen    = new Queue<CardPickChosenEvent>();
        readonly Queue<CardPickCancelledEvent> _cancelled = new Queue<CardPickCancelledEvent>();
        readonly Queue<int> _forced  = new Queue<int>();   // reqEntity: раскопка без окна (авто-ролл)
        readonly Queue<int> _expired = new Queue<int>();   // PlayerId, чей ход закончился (см. CardPickExpiredEvent)
        readonly HashSet<string> _poolCreationRequested = new HashSet<string>();

        public void Init(IEcsSystems systems)
        {
            GameEventBus.Subscribe<CardPickChosenEvent>(e => _chosen.Enqueue(e));
            GameEventBus.Subscribe<CardPickCancelledEvent>(e => _cancelled.Enqueue(e));
            // Момент истечения задаёт CardPickBrokerSystem (одно правило на все каналы пика), способ —
            // наш: раскопка форсит случайный вариант из предложенного, как Йогг-Сарон.
            GameEventBus.Subscribe<CardPickExpiredEvent>(e => _expired.Enqueue(e.PlayerId));
            _subscribed = true;
        }
        public void Run(IEcsSystems systems)
        {
            // РЕШАЕТ СИМУЛЯТОР ХОДА, а не владелец карты. Раньше маршрут шёл по OwnCardTag, и раскопка,
            // сработавшая ВНЕ хода владельца (хрип Королевской пиньяты в ход оппонента разыграл её
            // «Приглашение»), висела вечно у ОБОИХ: владельцу окно показать негде (не его ход), а
            // симулятор ждал его выбора из стора, которого никто не пришлёт. EndTurnRequestSystem Шаг B
            // ждёт пустого DiscoverRequestComponent → ход не передавался = СОФТ-ЛОК (лог 2026-08-03, PvE).
            // Та же болезнь и то же лекарство, что у Selected-целей: не можешь спросить — ролль
            // (RunAbilityTargetingSystem, «форс random вне хода владельца»).
            bool simulator = TurnGate.IsLocalActive(_world.Value);

            // Копим в буфер: обработка может создавать/удалять сущности (CreateCardEvent / DelEntity).
            var pending = new List<int>();
            foreach (var req in _reqFilter.Value) pending.Add(req);
            foreach (var req in pending)
            {
                if (!_reqPool.Value.Has(req)) continue;
                if (simulator) TryDecide(req);
                else           TryResolveRemote(req);   // пассив ждёт решения симулятора (стор реплея)
            }

            while (_chosen.Count > 0)    ResolveChosen(_chosen.Dequeue());
            while (_forced.Count > 0)    ForceResolveDiscover(_forced.Dequeue());
            while (_cancelled.Count > 0) ResolveCancelled(_cancelled.Dequeue());

            // Ход закончился, а раскопка ещё висит (окно открыто/не открыто, игрок не успел выбрать) — окно
            // НЕ должно пережить конец хода (баг: зависало навсегда). Форсим случайный выбор из предложенного,
            // как ForceRandomTargetingComponent у Йогг-Сарон-эффектов.
            while (_expired.Count > 0) ExpireForPlayer(_expired.Dequeue());
        }

        // Все СВОИ (не чужие-из-реплея) discover-запросы владельца, чей ход закончился, — форс-резолв
        // случайным выбором из уже предложенного (или предложить и сразу выбрать, если ещё не успели оффернуть).
        // Идём по ИГРОКУ, а не по выданным талонам: свернуть надо и те запросы, что окна ещё не получали
        // (вторая раскопка одного каста ждёт своей очереди в HasEarlierPending).
        void ExpireForPlayer(int playerId)
        {
            var pending = new List<int>();
            foreach (var req in _reqFilter.Value) pending.Add(req);
            foreach (var req in pending)
            {
                if (!_reqPool.Value.Has(req)) continue;
                ref var r = ref _reqPool.Value.Get(req);
                if (!_ownPool.Value.Has(r.SourceCardEntity)) continue;   // чужие резолвятся из CardPickReplayStore
                if (!_playerPool.Value.Has(r.OwnerPlayerEntity) || _playerPool.Value.Get(r.OwnerPlayerEntity).PlayerId != playerId) continue;
                ForceResolveDiscover(req);
            }
        }

        void ForceResolveDiscover(int reqEntity)
        {
            if (!_reqPool.Value.Has(reqEntity)) return;
            ref var r = ref _reqPool.Value.Get(reqEntity);

            if (!r.Offered)
            {
                int[] tokens; string[] exp; int[] ids; CardVisualData[] visuals;
                if (r.FromPool) BuildPoolOffer(r, out tokens, out exp, out ids, out visuals);
                else            BuildZoneOffer(r, out tokens, out exp, out ids, out visuals);
                if (tokens.Length == 0) { _world.Value.DelEntity(reqEntity); return; }   // фуззл
                r.Offered = true; r.ShownTokens = tokens; r.ShownExp = exp; r.ShownCardId = ids;
            }
            if (r.ShownTokens == null || r.ShownTokens.Length == 0) { _world.Value.DelEntity(reqEntity); return; }

            Apply(reqEntity, r.ShownTokens[UnityEngine.Random.Range(0, r.ShownTokens.Length)]);
        }

        // Владелец в СВОЁМ ходу? (ActiveState или каскад начала/конца). Копия хелпера из
        // RunAbilityTargetingSystem: TriggerUtil.IsOwnersTurn живёт в Game.Core.Ability, на которую
        // сборка систем не ссылается. Проверяем сущность ВЛАДЕЛЬЦА, а не TurnGate.IsLocalActive: в PvE
        // один клиент симулирует обоих, и тот гейт истинен и в ход ИИ.
        bool IsOwnersTurn(int playerEntity)
        {
            if (playerEntity < 0) return false;
            return _activePool.Value.Has(playerEntity)
                || _world.Value.GetPool<StartTurnState>().Has(playerEntity)
                || _world.Value.GetPool<EndTurnState>().Has(playerEntity);
        }

        // Есть ли БОЛЕЕ РАННИЙ (меньший Seq) живой запрос того же источника: раскопки одного каста идут
        // СТРОГО по очереди («Приглашение»: два окна) — иначе корреляция выбора по SourceCardEntity
        // неоднозначна, а single-slot CardPickReplayStore у пассива терял бы первый выбор.
        bool HasEarlierPending(int reqEntity)
        {
            ref var r = ref _reqPool.Value.Get(reqEntity);
            foreach (var other in _reqFilter.Value)
            {
                if (other == reqEntity || !_reqPool.Value.Has(other)) continue;
                ref var o = ref _reqPool.Value.Get(other);
                if (o.SourceCardEntity == r.SourceCardEntity && o.Seq < r.Seq) return true;
            }
            return false;
        }

        // ── Решение симулятора: окно владельцу либо ролл за него ─────────────────
        void TryDecide(int reqEntity)
        {
            ref var r = ref _reqPool.Value.Get(reqEntity);
            if (r.Offered) return;
            if (HasEarlierPending(reqEntity)) return;                  // ждём резолва предыдущей раскопки источника

            // Окно возможно ТОЛЬКО у владельца и ТОЛЬКО в его ход. Во всех прочих случаях выбор делает
            // симулятор случайным роллом (как Йогг-Сарон):
            //   • ForceRandomTargeting — авто-розыгрыш by design не отдаёт выбор игроку;
            //   • чужая карта — за владельца симулятор выбирать не может, а спросить его нечем;
            //   • НЕ ход владельца (хрип в чужой ход) — игрок физически не может кликать не в свой ход.
            // Окно не нужно → слот у брокера НЕ занимаем (иначе висел бы впустую).
            bool autoRoll = _world.Value.GetPool<ForceRandomTargetingComponent>().Has(r.SourceCardEntity)
                         || !_ownPool.Value.Has(r.SourceCardEntity)
                         || !IsOwnersTurn(r.OwnerPlayerEntity);

            // Ход владельца ИДЁТ, но ActiveState ещё нет (каскад начала хода) — окно откроем, когда он
            // появится. Роллить здесь рано: момент выбора у игрока просто не наступил.
            if (!autoRoll && !_activePool.Value.Has(r.OwnerPlayerEntity)) return;

            // Окно выбора одно на всю игру и его делят четыре канала: публиковать оффер можно только
            // получив слот у CardPickBrokerSystem, иначе соседний канал перебьёт наш показ в том же кадре
            // (Развилка + Адовый червь). Первый вызов ставит талон, слот придёт следующим кадром.
            if (!autoRoll && !PickTicket.Ready(_world.Value, reqEntity, ref r.RequestId, r.OwnerPlayerEntity))
                return;

            int[] tokens; string[] exp; int[] ids; CardVisualData[] visuals;
            if (r.FromPool) BuildPoolOffer(r, out tokens, out exp, out ids, out visuals);
            else            BuildZoneOffer(r, out tokens, out exp, out ids, out visuals);

            if (tokens.Length == 0) { _world.Value.DelEntity(reqEntity); return; }   // фуззл (талон уйдёт с сущностью)

            r.Offered     = true;
            r.ShownTokens = tokens;
            r.ShownExp    = exp;
            r.ShownCardId = ids;

            // Без окна: роллим в общем порядке резолва (Run), а не здесь — иначе Apply удалял бы сущность
            // прямо посреди обхода фильтра запросов.
            if (autoRoll) { _forced.Enqueue(reqEntity); return; }

            GameEventBus.Publish(new CardPickOfferedEvent
            {
                RequestId           = r.RequestId,          // корреляция (эхо в Chosen/Cancelled)
                CastingCardEntity   = r.SourceCardEntity,
                PlayerEntity        = r.OwnerPlayerEntity,
                OfferedCardEntities = tokens,
                OfferedCardVisuals  = visuals,
                OfferedCount        = tokens.Length,
                AllowCancel         = true,   // от раскопки можно отказаться (ResolveCancelled)
            });
        }

        void BuildZoneOffer(in DiscoverRequestComponent r, out int[] tokens, out string[] exp, out int[] ids, out CardVisualData[] visuals)
        {
            var candidates = TargetGather.Gather(_world.Value, r.Filters, r.SourceCardEntity, r.OwnerPlayerEntity, null, r.Zone);
            TakeRandom(candidates, r.OfferCount);
            int n = candidates.Count;
            tokens = new int[n]; exp = new string[n]; ids = new int[n]; visuals = new CardVisualData[n];
            for (int i = 0; i < n; i++)
            {
                int e = candidates[i];
                tokens[i] = e;   // реальная сущность зоны
                if (_modelPool.Value.Has(e))
                {
                    ref var m = ref _modelPool.Value.Get(e);
                    exp[i] = m.ExpansionId; ids[i] = m.ModelId;
                    var inst = _cardConfig.Value.Get(m.ExpansionId, m.ModelId);
                    if (inst?.CardData != null) visuals[i] = CardVisualDataFactory.From(inst.CardData);
                }
            }
        }

        void BuildPoolOffer(in DiscoverRequestComponent r, out int[] tokens, out string[] exp, out int[] ids, out CardVisualData[] visuals)
        {
            int total = r.PoolExp != null ? r.PoolExp.Length : 0;
            var idx = new List<int>(total);
            for (int i = 0; i < total; i++) idx.Add(i);
            TakeRandom(idx, r.OfferCount);

            int n = idx.Count;
            tokens = new int[n]; exp = new string[n]; ids = new int[n]; visuals = new CardVisualData[n];
            for (int i = 0; i < n; i++)
            {
                int p = idx[i];
                tokens[i] = -(i + 1);            // синтетический токен (карты-сущности ещё нет)
                exp[i]    = r.PoolExp[p];
                ids[i]    = r.PoolCardId[p];
                var inst  = _cardConfig.Value.Get(exp[i], ids[i]);
                if (inst?.CardData != null) visuals[i] = CardVisualDataFactory.From(inst.CardData);
            }
        }

        // ── Не мы решаем (реплей): авто-резолв из стора ──────────────────────────
        // Сюда попадает и СВОЯ карта, если решает не этот клиент (наш хрип сработал в ход оппонента —
        // выбор делает симулятор и присылает его обычным каналом пика).
        void TryResolveRemote(int reqEntity)
        {
            if (HasEarlierPending(reqEntity)) return;   // очередь: выбор в сторе принадлежит БОЛЕЕ РАННЕЙ раскопке
            ref var r = ref _reqPool.Value.Get(reqEntity);
            string srcKey = NetKey(r.SourceCardEntity);
            if (!CardPickReplayStore.TryPeek(srcKey, out var choice)) return;

            if (choice.CreateFromPool)
            {
                if (!_state.Value.TryGetEntity(choice.ChosenEntityKey, out _))
                {
                    if (!_poolCreationRequested.Contains(choice.ChosenEntityKey))
                    {
                        // isEnemy считаем от ВЛАДЕЛЬЦА, а не константой true: по этой ветке теперь идёт и
                        // СВОЯ карта (решение принимал симулятор) — с жёстким true своя карта приехала бы
                        // в руку помеченной вражеской.
                        bool isEnemy = !(_playerPool.Value.Has(r.OwnerPlayerEntity)
                                      && _playerPool.Value.Get(r.OwnerPlayerEntity).IsLocalPlayer);
                        GeneratedModScratch.Register(choice.ChosenEntityKey, r.SourceCardEntity, r.Modifiers);   // зеркально активу
                        SpawnToHand(r.OwnerPlayerEntity, r.OwnerId, choice.ExpansionId, choice.CardId, choice.ChosenEntityKey, isEnemy, r.SourceCardEntity);
                        _poolCreationRequested.Add(choice.ChosenEntityKey);
                    }
                    return;   // ждём появления сущности
                }
                // создана (InHand=true) — готово
            }
            else
            {
                if (!_state.Value.TryGetEntity(choice.ChosenEntityKey, out int chosen)) return;   // ещё нет — ждём
                PlacePicked(chosen, r);
            }

            CardPickReplayStore.Remove(srcKey);
            _poolCreationRequested.Remove(choice.ChosenEntityKey);
            _world.Value.DelEntity(reqEntity);
        }

        // ── Резолв выбора игрока (актив) ─────────────────────────────────────────

        // Матч ТОЛЬКО по токену окна. Прежняя эвристика (источник + вхождение токена в предложение +
        // наименьший Seq) была нужна лишь потому, что несколько запросов могли делить один
        // CastingCardEntity, а шину слушают все каналы пика разом. С RequestId кандидат ровно один.
        void ResolveChosen(CardPickChosenEvent e)
        {
            if (e.RequestId == 0) return;
            foreach (var reqEntity in _reqFilter.Value)
            {
                if (_reqPool.Value.Get(reqEntity).RequestId != e.RequestId) continue;
                Apply(reqEntity, e.ChosenCardEntity);
                return;   // Apply удаляет сущность — обход фильтра дальше не продолжаем
            }
        }

        // Терминал раскопки: выбранный токен → карта в Dest + синк выбора пассиву. Общий для окна,
        // авто-ролла (ForceRandomTargeting) и форс-резолва на конце хода.
        void Apply(int reqEntity, int chosenToken)
        {
            if (!_reqPool.Value.Has(reqEntity)) return;

            // Снимаем КОПИЮ до побочных эффектов: SpawnToHand/PlacePicked публикуют события и применяют
            // модификаторы, а те могут породить новую раскопку — пул DiscoverRequestComponent переедет
            // в памяти и удержанный ref «поплывёт». Структура из ссылок, копия дешёвая.
            var r = _reqPool.Value.Get(reqEntity);

            int idx = IndexOf(r.ShownTokens, chosenToken);
            if (idx < 0) return;   // токен не из этого предложения

            int source = r.SourceCardEntity;

            if (r.FromPool)
            {
                string exp = r.ShownExp[idx];
                int    cid = r.ShownCardId[idx];
                // ПУЛ: карта создаётся заново → генерируем новый короткий общий ключ (не геттер NetKey(entity)!).
                // Полное имя — локальный метод NetKey(int) перекрывает простое имя класса.
                string key = Game.Core.Network.NetKey.Next();
                // NB: цели у карты, разыгранной модификатором дискавера, выбираются СЛУЧАЙНО — это
                // ЗАДУМАНО («Дополнительная возможность» форсит random, как Йогг-Сарон), а не баг.
                GeneratedModScratch.Register(key, source, r.Modifiers);   // мод-ры применит CreateCardSystem
                SpawnToHand(r.OwnerPlayerEntity, r.OwnerId, exp, cid, key, isEnemy: false, sourceCard: source);
                EmitResolved(source, chosenEntity: -1, fromPool: true, key, exp, cid);
            }
            else
            {
                PlacePicked(chosenToken, r);
                EmitResolved(source, chosenToken, fromPool: false, NetKey(chosenToken), null, -1);
            }

            _world.Value.DelEntity(reqEntity);   // талон окна уходит вместе с сущностью → слот свободен
        }

        void ResolveCancelled(CardPickCancelledEvent e)
        {
            if (e.RequestId == 0) return;
            foreach (var reqEntity in _reqFilter.Value)
            {
                if (_reqPool.Value.Get(reqEntity).RequestId != e.RequestId) continue;
                _world.Value.DelEntity(reqEntity);   // отменена → ничего не получаем, слот освобождён
                return;
            }
        }

        // ── helpers ──────────────────────────────────────────────────────────────

        // ТЕРМИНАЛ discover: отправить выбранную СУЩЕСТВУЮЩУЮ карту в Dest (Hand/Deck/Grave), кому — по
        // TakeOwnership (true → касательеру, воровство; false → текущему владельцу). Снимает с прежней зоны
        // и списков, при воровстве меняет OwnerComponent + Own/EnemyCardTag (клиент-относительные).
        void PlacePicked(int card, in DiscoverRequestComponent r)
        {
            int curOwner = OwnerId(card);
            bool wasInHand = _handTagPool.Value.Has(card);

            // снять с текущей зоны (теги + списки прежнего владельца)
            RemoveFromZoneLists(card, curOwner);
            if (_deckTagPool.Value.Has(card))  _deckTagPool.Value.Del(card);
            if (_handTagPool.Value.Has(card))  _handTagPool.Value.Del(card);
            if (_graveTagPool.Value.Has(card)) _graveTagPool.Value.Del(card);
            // Сайдборд (Сказочник): карта уходит из отложенных НАВСЕГДА — обратно её вернуть нечем,
            // и это правильно, зона одноразовая. Без снятия тега карта осталась бы и в руке, и в
            // сайдборде разом, и раскопка предложила бы её второй раз.
            if (_sideboardTagPool.Value.Has(card)) _sideboardTagPool.Value.Del(card);

            // воровство: владелец → кастер (+ свопнуть клиент-относительные теги)
            int destOwnerId = r.TakeOwnership ? r.OwnerId : curOwner;
            if (r.TakeOwnership && curOwner != r.OwnerId)
            {
                if (_ownerPool.Value.Has(card)) _ownerPool.Value.Get(card).OwnerId = r.OwnerId;
                bool wasOwn = _ownTagPool.Value.Has(card);
                if (wasOwn) { _ownTagPool.Value.Del(card); if (!_enemyTagPool.Value.Has(card)) _enemyTagPool.Value.Add(card); }
                else        { if (_enemyTagPool.Value.Has(card)) _enemyTagPool.Value.Del(card); if (!_ownTagPool.Value.Has(card)) _ownTagPool.Value.Add(card); }

                // ХС-семантика: способности украденной карты «служат» вору (триггеры/бенефициар/таргетинг —
                // пере-привязка; та же логика, что у StealToHandEffect/TakeControlEffect).
                AbilityRebindUtil.Rebind(_world.Value, card, r.OwnerPlayerEntity);
            }

            int destPlayer = FindPlayerEntity(destOwnerId);

            // Модификаторы выбранной карты («стоит на 2 меньше» и т.п.) — ДО CardDrawnEvent, чтобы UI руки
            // сразу увидел изменённую стоимость. PlacePicked идёт на обоих клиентах → применение зеркальное.
            if (r.Modifiers != null)
                foreach (var mod in r.Modifiers)
                    if (mod != null && mod.IsReady)
                        mod.Apply(_world.Value, r.SourceCardEntity, card);

            // Модификатор мог УВЕСТИ карту прямо здесь («выберите и разыграйте» — PlayTargetCardEffect,
            // «Приглашение»): существо уже едет на борд (MoveCardToBoardEvent), спелл/чара ждёт авто-каста
            // (AutoCastComponent). Регистрировать такую карту в Dest-зоне нельзя — фантом руки/колоды
            // (тот же случай, что StillInDeclaredZone у CreateCardSystem). ТРЕТИЙ случай — RememberCardFor-
            // LaterPlayEffect (Контрабандист/Королевский шут): карта должна остаться ЗОНО-НЕЗАВИСИМОЙ до
            // OnDie (см. RememberedPlayTargetComponent) — у FromPool-раскопки это уже работает (материализация
            // идёт через CreateCardSystem, у которого есть тот же гейт), а у DiscoverFromZoneEffect карта
            // СУЩЕСТВУЮЩАЯ и идёт через PlacePicked — без этой проверки Dest ниже тут же возвращал её в
            // руку/колоду/кладбище, стирая эффект «запомнить» (баг: Контрабандист клал карту в руку сразу,
            // а не по смерти).
            var rememberPool = _world.Value.GetPool<RememberedPlayTargetComponent>();
            bool leftByModifier = _world.Value.GetPool<MoveCardToBoardEvent>().Has(card)
                               || _world.Value.GetPool<AutoCastComponent>().Has(card)
                               || (rememberPool.Has(r.SourceCardEntity) && rememberPool.Get(r.SourceCardEntity).Entity == card);

            if (!leftByModifier)
                switch (r.Dest)
                {
                    case DiscoverDest.Hand:
                        // Лимит руки (единое правило, HandSpace): места нет → выбранная карта СГОРАЕТ.
                        // Зональные теги с неё уже сняты выше, вернуть «как было» нельзя; выбор потрачен.
                        if (!HandSpace.HasRoom(_world.Value, destPlayer))
                        {
                            HandSpace.Burn(_world.Value, card, "выбор дискавера в руку");
                            break;
                        }
                        if (!_handTagPool.Value.Has(card)) _handTagPool.Value.Add(card);
                        AddToHandList(destPlayer, card);
                        // SourceEntity = кастер раскопки — UI летит визуально ОТ него, а не «из-за края экрана»
                        // (см. HandUISystem.TryGet), как у MoveToHandEffect.
                        GameEventBus.Publish(new CardDrawnEvent { CardEntity = card, PlayerId = destPlayer, SourceEntity = r.SourceCardEntity });
                        break;
                    case DiscoverDest.Deck:
                        if (!_deckTagPool.Value.Has(card)) _deckTagPool.Value.Add(card);
                        AddToDeckList(destPlayer, card);
                        break;
                    case DiscoverDest.Grave:
                        if (!_graveTagPool.Value.Has(card)) _graveTagPool.Value.Add(card);
                        break;
                }

            // если карта ушла ИЗ руки (сброс/замешивание/воровство/розыгрыш модификатором) — обновить UI руки
            if (wasInHand && (leftByModifier || r.Dest != DiscoverDest.Hand))
                GameEventBus.Publish(new CardRemovedFromHandUIEvent { CardEntity = card });
        }

        void RemoveFromZoneLists(int card, int ownerId)
        {
            int pe = FindPlayerEntity(ownerId);
            if (pe < 0) return;
            if (_deckPool.Value.Has(pe))
            {
                ref var deck = ref _deckPool.Value.Get(pe);
                if (deck.CardEntities != null && deck.CardEntities.Remove(card)) deck.Count = deck.CardEntities.Count;
            }
            if (_handPool.Value.Has(pe))
            {
                ref var hand = ref _handPool.Value.Get(pe);
                if (hand.CardEntities != null && hand.CardEntities.Remove(card)) hand.Count = hand.CardEntities.Count;
            }
        }

        void AddToHandList(int player, int card)
        {
            if (player < 0 || !_handPool.Value.Has(player)) return;
            ref var hand = ref _handPool.Value.Get(player);
            hand.CardEntities ??= new List<int>();
            if (!hand.CardEntities.Contains(card)) hand.CardEntities.Add(card);
            hand.Count = hand.CardEntities.Count;
        }

        void AddToDeckList(int player, int card)
        {
            if (player < 0 || !_deckPool.Value.Has(player)) return;
            ref var deck = ref _deckPool.Value.Get(player);
            deck.CardEntities ??= new List<int>();
            if (!deck.CardEntities.Contains(card)) deck.CardEntities.Add(card);
            deck.Count = deck.CardEntities.Count;
        }

        int OwnerId(int card) => _ownerPool.Value.Has(card) ? _ownerPool.Value.Get(card).OwnerId : -1;

        int FindPlayerEntity(int ownerId)
        {
            if (ownerId < 0) return -1;
            foreach (var pe in _playerFilter.Value)
                if (_playerPool.Value.Get(pe).PlayerId == ownerId) return pe;
            return -1;
        }

        // Создать НОВУЮ карту (пул) сразу в руке владельца с заданным ключом.
        void SpawnToHand(int ownerPlayer, int ownerId, string exp, int cardId, string key, bool isEnemy, int sourceCard)
        {
            if (string.IsNullOrEmpty(exp) || cardId < 0) return;
            GameEventBus.Publish(new CreateCardEvent
            {
                ExpansionId        = exp,
                CardId             = cardId,
                NetworkEntityKey   = key,
                PlayerOwnerEntity  = ownerPlayer,
                OwnerId            = ownerId,
                IsEnemy            = isEnemy,
                InHand             = true,
                RegisterInZoneList = true,
                SourceEntity       = sourceCard,   // UI летит визуально ОТ кастера раскопки, а не «из-за края экрана»
            });
        }

        void EmitResolved(int sourceCard, int chosenEntity, bool fromPool, string chosenKey, string exp, int cardId)
        {
            int modelId = (chosenEntity >= 0 && _modelPool.Value.Has(chosenEntity))
                ? _modelPool.Value.Get(chosenEntity).ModelId : cardId;

            GameEventBus.Publish(new CardPickResolvedNetEvent
            {
                CastingCardEntity     = sourceCard,
                CastingCardNetworkKey = NetKey(sourceCard),
                ChosenCardEntity      = chosenEntity,
                ChosenCardModelId     = modelId,
                ChosenCardNetworkKey  = chosenKey,
                CreateFromPool        = fromPool,
                ChosenExpansionId     = exp,
                ChosenCardId          = cardId,
            });
        }

        static int IndexOf(int[] arr, int v)
        {
            if (arr == null) return -1;
            for (int i = 0; i < arr.Length; i++) if (arr[i] == v) return i;
            return -1;
        }

        // discover: оставить в списке случайный поднабор размера n (n<=0 или >=всего → весь список).
        static void TakeRandom(List<int> src, int n)
        {
            if (n <= 0 || src.Count <= n) return;
            for (int i = 0; i < n; i++)
            {
                int j = UnityEngine.Random.Range(i, src.Count);
                (src[i], src[j]) = (src[j], src[i]);
            }
            src.RemoveRange(n, src.Count - n);
        }

        string NetKey(int entity)
            => (entity >= 0 && _netPool.Value.Has(entity)) ? _netPool.Value.Get(entity).NetworkEntityKey : null;

    }
}
