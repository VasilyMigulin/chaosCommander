using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Mono;
using UnityEngine;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// Уводит существо с поля по LeaveBoardEvent (баунс/баниш/замешивание): снимает BoardTag/позицию/визуал,
    /// вешает тег зоны назначения, обновляет списки руки/колоды владельца. Для руки/колоды сбрасывает статы
    /// (Current=Max, чистит мягкие модификаторы) — возвращается «свежим». НЕ публикует CreatureDiedEvent
    /// (не гибель). Работает на ОБОИХ клиентах (эффект детерминирован по цели → оба уводят одинаково).
    /// </summary>
    public sealed class RunLeaveBoardSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsCustomInject<BoardView> _boardView = default;
        readonly EcsFilterInject<Inc<LeaveBoardEvent>> _filter = default;
        readonly EcsPoolInject<LeaveBoardEvent> _leavePool = default;
        readonly EcsPoolInject<BoardTag> _boardPool = default;
        readonly EcsPoolInject<BoardPositionComponent> _posPool = default;
        readonly EcsPoolInject<SelectTag> _selectPool = default;
        readonly EcsPoolInject<HandTag> _handTagPool = default;
        readonly EcsPoolInject<DeckTag> _deckTagPool = default;
        readonly EcsPoolInject<GraveTag> _graveTagPool = default;
        readonly EcsPoolInject<LimboTag> _limboTagPool = default;
        readonly EcsPoolInject<ViewRefComponent> _viewPool = default;
        readonly EcsPoolInject<ViewSpawnedTag> _spawnedPool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsPoolInject<HandComponent> _handPool = default;
        readonly EcsPoolInject<DeckComponent> _deckPool = default;
        readonly EcsPoolInject<AttackComponent> _atkPool = default;
        readonly EcsPoolInject<HealthComponent> _hpPool = default;
        readonly EcsPoolInject<SpeedComponent> _speedPool = default;
        readonly EcsPoolInject<DeadTag> _deadPool = default;
        readonly EcsPoolInject<CommanderTag> _commanderPool = default;
        readonly EcsPoolInject<AttacksUsedComponent> _attacksUsedPool = default;
        readonly EcsPoolInject<TokenTag> _tokenPool = default;            // токен при полной руке → лимбо, не кладбище
        readonly EcsPoolInject<CardModelComponent> _modelPool = default;  // имя карты в логе
        readonly EcsPoolInject<BoardEntryOrderComponent> _entryOrderPool = default;   // порядок выхода на стол
        readonly EcsPoolInject<DieEvent> _diePool = default;              // хрипы при гибели «рука полна»

        int EntrySeq(int entity)
            => _entryOrderPool.Value.Has(entity) ? _entryOrderPool.Value.Get(entity).Seq : 0;

        readonly EcsFilterInject<Inc<PlayerComponent>> _players = default;

        readonly System.Collections.Generic.List<int> _batch = new();

        public void Run(IEcsSystems systems)
        {
            // ПОРЯДОК ухода с поля = порядок ВЫХОДА на стол («первым вышел — первым вернулся в руку»,
            // требование юзера 2026-07-31). Массовый баунс («Призвать ураган») обрабатывает пачку за раз, а
            // порядок ecslite-фильтра произвольный (свап-удаления) — без сортировки карты возвращались как
            // попало, и при полной руке погибали НЕ те. Штамп — тот же BoardEntryOrderComponent, что задаёт
            // очерёдность активаций способностей; нет штампа (карта не была на столе) → 0, уходит первой.
            _batch.Clear();
            foreach (var e in _filter.Value) _batch.Add(e);
            _batch.Sort((a, b) => EntrySeq(a).CompareTo(EntrySeq(b)));

            foreach (var entity in _batch)
            {
                if (!_leavePool.Value.Has(entity)) continue;   // событие могло исчезнуть в этой же пачке
                var dest = _leavePool.Value.Get(entity).Destination;

                // Мировая позиция ДО зачистки ниже (BoardPositionComponent сейчас ещё на месте) — если карта
                // едет в руку (баунс/командир), UI должен знать, ОТКУДА она визуально летит, а не с дефолтной
                // точки «из-за края экрана» (см. HandUISystem/CardLayout). Для Deck/Grave/Limbo не нужна.
                Vector3? fromWorld = null;
                if (EntityWorldPosUtil.TryGet(_world.Value, _boardView.Value, entity, out var srcPos))
                    fromWorld = srcPos;

                // Командир НИКОГДА не уходит в колоду/кладбище/лимбо: любой уход с поля (баунс/замешать/баниш —
                // Призвать ураган, Закопать заживо и т.п.) для командира = возврат в РУКУ (как при смерти).
                // Иначе командир теряется в колоде/изгнании. КД смерти тут не вешаем (это не гибель).
                if (_commanderPool.Value.Has(entity)) dest = LeaveDestination.Hand;

                // Существо могло уйти с поля, будучи ВЫБРАННЫМ игроком для хода/атаки (баунс всей доски —
                // «Призвать ураган» — не только атакующий/цели способности), RunSelectCellSystem держит его
                // как selectedEntity и на следующий клик падает — BoardPositionComponent уже снята ниже.
                // DieSystem подчищает SelectTag так же (см. ProcessDeath) — тот же класс бага для смерти.
                if (_selectPool.Value.Has(entity))
                {
                    _selectPool.Value.Del(entity);
                    GameEventBus.Publish(new CreatureDeselectedEvent());
                    if (_boardView.Value != null)
                    {
                        _boardView.Value.ClearAllHighlights(1);
                        _boardView.Value.ClearAllHighlights(2);
                        _boardView.Value.GetAvatarCell(1)?.SetHighlight(CellHighlight.None);
                        _boardView.Value.GetAvatarCell(2)?.SetHighlight(CellHighlight.None);
                    }
                }

                // Уже был в кладбище ДО этого хода (Скелетон «при смерти→в руку»: DieSystem уже отправил его
                // в GraveTag и уже отпубликовал CreatureDiedEvent один раз, ПРЕЖДЕ чем его же OnDie-хрип
                // попытался баунснуть его в руку) — см. использование ниже (hand-full фолбэк).
                bool cameFromGrave = _graveTagPool.Value.Has(entity);

                // снять с текущей зоны (любой — Скелетон «при смерти→в руку» уже в кладбище через DieSystem)
                if (_boardPool.Value.Has(entity))    _boardPool.Value.Del(entity);
                if (_deckTagPool.Value.Has(entity))  _deckTagPool.Value.Del(entity);
                if (_graveTagPool.Value.Has(entity)) _graveTagPool.Value.Del(entity);
                if (_limboTagPool.Value.Has(entity)) _limboTagPool.Value.Del(entity);
                if (_posPool.Value.Has(entity))   _posPool.Value.Del(entity);
                if (_viewPool.Value.Has(entity))
                {
                    ref var vr = ref _viewPool.Value.Get(entity);
                    if (vr.View != null) Object.Destroy(vr.View);
                    vr.View = null;
                }
                if (_spawnedPool.Value.Has(entity)) _spawnedPool.Value.Del(entity);

                switch (dest)
                {
                    case LeaveDestination.Hand:
                        // РУКА ПОЛНА → карта УНИЧТОЖАЕТСЯ (правило ХС для баунса; баг 2026-07-31: «Призвать
                        // ураган» возвращал ВСЕХ, и в руке оказывалось 7 карт при лимите 6). Токен — в лимбо
                        // (он и так не идёт на кладбище). Командир — исключение: у него отдельный слот, он
                        // возвращается всегда.
                        if (!_commanderPool.Value.Has(entity)
                            && !HandSpace.HasRoomForOwner(_world.Value, OwnerIdOf(entity)))
                        {
                            HandSpace.Burn(_world.Value, entity, "баунс в полную руку");

                            // ПРЕДСМЕРТНЫЕ ХРИПЫ СРАБАТЫВАЮТ — но ТОЛЬКО если это первая гибель (живое
                            // существо не смогло уйти в руку, напр. «Призвать ураган»). Если карта попала
                            // сюда УЖЕ БУДУЧИ в кладбище (cameFromGrave — Скелетон «при смерти→в руку»:
                            // DieSystem уже отправил её в грав и один раз опубликовал CreatureDiedEvent) —
                            // повторная публикация здесь снова триггерит ТОТ ЖЕ OnDie-хрип «вернись в руку»,
                            // рука всё ещё полна → снова Burn → снова CreatureDiedEvent → БЕСКОНЕЧНЫЙ ЦИКЛ
                            // (баг 2026-08-19). Карта и так уже в кладбище (Burn выше) — хрипы своё уже
                            // отыграли на настоящей смерти, второй раз им играть нечего.
                            if (!cameFromGrave)
                            {
                                if (!_diePool.Value.Has(entity)) _diePool.Value.Add(entity);
                                GameEventBus.Publish(new CreatureDiedEvent { CardEntity = entity, KillerEntity = -1 });
                            }
                            break;
                        }

                        ResetStats(entity);
                        if (!_handTagPool.Value.Has(entity)) _handTagPool.Value.Add(entity);
                        AddToZoneList(entity, toHand: true, fromWorld);
                        break;
                    case LeaveDestination.Deck:
                        ResetStats(entity);
                        if (!_deckTagPool.Value.Has(entity)) _deckTagPool.Value.Add(entity);
                        AddToZoneList(entity, toHand: false, fromWorld);
                        break;
                    case LeaveDestination.Grave:
                        if (!_graveTagPool.Value.Has(entity)) _graveTagPool.Value.Add(entity);
                        break;
                    default:   // Limbo (баниш) — вне игры
                        if (!_limboTagPool.Value.Has(entity)) _limboTagPool.Value.Add(entity);
                        break;
                }

                _leavePool.Value.Del(entity);
            }
        }

        int OwnerIdOf(int entity)
            => _ownerPool.Value.Has(entity) ? _ownerPool.Value.Get(entity).OwnerId : -1;

        void ResetStats(int entity)
        {
            // ВОСКРЕШЕНИЕ при возврате в руку/колоду: Скелетон уходит в кладбище через DieSystem с DeadTag
            // (DieSystem снимает DeadTag только командиру), а потом баунсится «при смерти» сюда. Если не снять
            // DeadTag — карта вернётся в руку «мёртвой», и при повторном розыгрыше DieSystem (DeadTag+BoardTag+
            // CreatureTag) убьёт её мгновенно. Возврат в руку/колоду = существо снова живо.
            if (_deadPool.Value.Has(entity)) _deadPool.Value.Del(entity);
            if (_atkPool.Value.Has(entity))   { ref var a = ref _atkPool.Value.Get(entity);   a.ClearModifiers(); }
            if (_hpPool.Value.Has(entity))    { ref var h = ref _hpPool.Value.Get(entity);     h.ClearModifiers(); h.Current = h.Max; }
            if (_speedPool.Value.Has(entity)) { ref var s = ref _speedPool.Value.Get(entity);  s.ClearModifiers(); s.Remaining = s.Max; }
            // Счётчик атак за ход — иначе вернувшееся существо не сможет атаковать в ход повторного розыгрыша
            // (сброс на старте хода касается только существ НА борде).
            if (_attacksUsedPool.Value.Has(entity)) _attacksUsedPool.Value.Get(entity).Value = 0;

            // Та же ловушка, что и со смертью командира (см. DieSystem.ProcessDeath): баунс — ТА ЖЕ ecs-
            // сущность, возвращается в игру и может быть переиграна. Без этого идемпотентность Tracked-ауры
            // («эту цель уже баффали») навсегда блокирует повторную выдачу — только тут это грозит ЛЮБОМУ
            // существу, не только командиру. Permanent-баффы RemoveTarget не трогает (симметрично). ДВА
            // параллельных механизма трекинга — снимаем из ОБОИХ (AppliedBuffs — тот же повод, для
            // ApplyTrackedBuffEffect, у которого раньше не было своего RemoveTarget вовсе, баг 2026-08-21).
            TrackedBuffs.RemoveTarget(_world.Value, entity);
            AppliedBuffs.RemoveTarget(_world.Value, entity);

            // Существо было ИСТОЧНИКОМ ауро-модификатора стоимости (AddCostAuraEffect) — в отличие от
            // AbilityAura.cs (см. кавеат ниже), этот механизм снимается и по баунсу, не только по OnDie.
            AuraCostModifiers.RemoveBySource(_world.Value, entity);

            // [SyncWatch] признанный кавеат ауры (AbilityAura.cs): реверт баффов, выданных ЭТИМ существом
            // как ИСТОЧНИКОМ ауры, привязан к OnDie — баунс в руку/колоду его не дёргает. Если тут есть
            // непустой трекинг — бафф на чужих существах остаётся навсегда, атака/HP входят в чексумму →
            // при асимметричном баунсе (одна сторона видит его раньше другой) это честный десинк.
            var trackedPool = _world.Value.GetPool<TrackedBuffsComponent>();
            var appliedPool = _world.Value.GetPool<AppliedBuffsComponent>();
            int trackedLeft = trackedPool.Has(entity) ? (trackedPool.Get(entity).Items?.Count ?? 0) : 0;
            int appliedLeft = appliedPool.Has(entity) ? (appliedPool.Get(entity).Records?.Count ?? 0) : 0;
            if (trackedLeft > 0 || appliedLeft > 0)
                UnityEngine.Debug.LogWarning($"[SyncWatch] existing={entity} баунсится с активной аурой-ИСТОЧНИКОМ "
                    + $"(TrackedBuffs.Items={trackedLeft}, AppliedBuffs.Records={appliedLeft}) — баффы НЕ отревертились (кавеат AbilityAura).");
        }

        void AddToZoneList(int entity, bool toHand, Vector3? fromWorld = null)
        {
            if (!_ownerPool.Value.Has(entity)) return;
            int ownerId = _ownerPool.Value.Get(entity).OwnerId;
            foreach (var pe in _players.Value)
            {
                if (_playerPool.Value.Get(pe).PlayerId != ownerId) continue;
                if (toHand && _handPool.Value.Has(pe))
                {
                    ref var hand = ref _handPool.Value.Get(pe);
                    hand.CardEntities ??= new System.Collections.Generic.List<int>();
                    if (!hand.CardEntities.Contains(entity)) hand.CardEntities.Add(entity);
                    hand.Count = hand.CardEntities.Count;
                    // UI (для своего игрока): FromWorld — снятая ДО зачистки позиция на столе (см. Run) —
                    // карта летит в руку от своего места, а не из-за края экрана, как обычный добор.
                    GameEventBus.Publish(new CardDrawnEvent { CardEntity = entity, PlayerId = pe, FromWorld = fromWorld });
                }
                else if (!toHand && _deckPool.Value.Has(pe))
                {
                    ref var deck = ref _deckPool.Value.Get(pe);
                    deck.CardEntities ??= new System.Collections.Generic.List<int>();
                    if (!deck.CardEntities.Contains(entity))
                    {
                        // «Втасовка» в колоду: позиция из ключа (детерминир. + синхронна), а не «в конец».
                        string key = _ownerPool.Value.Get(entity).EntityKey;
                        int idx = DeckShuffleUtil.InsertIndex(key, deck.CardEntities.Count);
                        deck.CardEntities.Insert(idx, entity);
                    }
                    deck.Count = deck.CardEntities.Count;
                }
                break;
            }
        }
    }
}
