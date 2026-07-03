using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
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
        readonly EcsFilterInject<Inc<LeaveBoardEvent>> _filter = default;
        readonly EcsPoolInject<LeaveBoardEvent> _leavePool = default;
        readonly EcsPoolInject<BoardTag> _boardPool = default;
        readonly EcsPoolInject<BoardPositionComponent> _posPool = default;
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

        readonly EcsFilterInject<Inc<PlayerComponent>> _players = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var entity in _filter.Value)
            {
                var dest = _leavePool.Value.Get(entity).Destination;

                // Командир НИКОГДА не уходит в колоду/кладбище/лимбо: любой уход с поля (баунс/замешать/баниш —
                // Призвать ураган, Закопать заживо и т.п.) для командира = возврат в РУКУ (как при смерти).
                // Иначе командир теряется в колоде/изгнании. КД смерти тут не вешаем (это не гибель).
                if (_commanderPool.Value.Has(entity)) dest = LeaveDestination.Hand;

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
                        ResetStats(entity);
                        if (!_handTagPool.Value.Has(entity)) _handTagPool.Value.Add(entity);
                        AddToZoneList(entity, toHand: true);
                        break;
                    case LeaveDestination.Deck:
                        ResetStats(entity);
                        if (!_deckTagPool.Value.Has(entity)) _deckTagPool.Value.Add(entity);
                        AddToZoneList(entity, toHand: false);
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
        }

        void AddToZoneList(int entity, bool toHand)
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
                    GameEventBus.Publish(new CardDrawnEvent { CardEntity = entity, PlayerId = pe });   // UI (для своего игрока)
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
