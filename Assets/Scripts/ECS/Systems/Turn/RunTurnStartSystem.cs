using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using UnityEngine;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// Каскад НАЧАЛА хода (одноразовая часть). На игроке со StartTurnState (Resolved=false):
    ///   1) восстановить скорость существ владельца;
    ///   2) начислить золото (доход по PersonalTurnNumber);
    ///   3) добор (DrawCardEvent);
    ///   4) сигнал начала хода (bus TurnStartedEvent) — его ловят OnTurnStartTrigger способностей;
    /// затем Resolved=true. ActiveState вешает RunActivateSystem, когда каскад осядет.
    /// Крутится только у активного (локального) клиента — на пассиве StartTurnState не вешается.
    /// </summary>
    public sealed class RunTurnStartSystem : IEcsRunSystem
    {
        readonly EcsPoolInject<StartTurnState>   _startPool  = default;
        readonly EcsPoolInject<PlayerComponent>  _playerPool = default;
        readonly EcsPoolInject<GoldComponent>    _goldPool   = default;
        readonly EcsPoolInject<ManaComponent>    _manaPool   = default;
        readonly EcsPoolInject<ManaFloorComponent> _manaFloorPool = default;   // Вечная попойка: пол маны
        readonly EcsPoolInject<DrawCardEvent>    _drawPool   = default;
        // Адовый червь: замена механики добора начала хода — вместо DrawCardEvent ставим маркер замены.
        readonly EcsPoolInject<DrawReplacementComponent>    _drawReplPool = default;
        readonly EcsPoolInject<DrawReplacementDueComponent> _drawDuePool  = default;
        readonly EcsPoolInject<LocalComponent>   _localPool  = default;
        readonly EcsFilterInject<Inc<StartTurnState, PlayerComponent>> _filter = default;

        readonly EcsFilterInject<Inc<CreatureTag, BoardTag, SpeedComponent, OwnerComponent>, Exc<DeadTag>> _creatures = default;
        readonly EcsPoolInject<SpeedComponent>   _speedPool  = default;
        readonly EcsPoolInject<OwnerComponent>   _ownerPool  = default;
        readonly EcsPoolInject<AttacksUsedComponent> _attacksUsedPool = default;

        readonly EcsFilterInject<Inc<AbilityContainerComponent, BoardTag, OwnerComponent>, Exc<HandTag, DeckTag>> _boardCards = default;
        readonly EcsPoolInject<TurnStartEvent>   _turnStartEventPool = default;

        readonly EcsFilterInject<Inc<CommanderTag, CommanderCooldownComponent, OwnerComponent>> _commanderCd = default;
        readonly EcsPoolInject<CommanderCooldownComponent> _commanderCdPool = default;

        // Наступающий кризис: игрок с GoldBlockComponent не получает доход золота в начале хода.
        readonly EcsPoolInject<GoldBlockComponent> _goldBlockPool = default;

        // Лимиты активаций способностей за ход (Ability.MaxActivationsPerTurn) — сброс на старте хода владельца.
        readonly EcsFilterInject<Inc<AbilityUseLimitComponent, AbilityOwnerComponent>> _abilityLimits = default;
        readonly EcsPoolInject<AbilityUseLimitComponent> _abilityLimitPool = default;
        readonly EcsPoolInject<AbilityOwnerComponent> _abilityOwnerPool = default;

        // Задачи: чары владельца на старте его хода («чары × ходов»).
        readonly EcsFilterInject<Inc<CharmTag, BoardTag, OwnerComponent>, Exc<DeadTag>> _charms = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var entity in _filter.Value)
            {
                ref var start = ref _startPool.Value.Get(entity);
                if (start.Resolved) continue;
                start.Resolved = true;

                int playerId = _playerPool.Value.Get(entity).PlayerId;

                // 1) Скорость существ владельца + сброс счётчика атак за ход (#4).
                foreach (var ce in _creatures.Value)
                {
                    if (_ownerPool.Value.Get(ce).OwnerId != playerId) continue;
                    ref var sp = ref _speedPool.Value.Get(ce);
                    sp.Remaining = sp.Max;
                    if (_attacksUsedPool.Value.Has(ce)) _attacksUsedPool.Value.Get(ce).Value = 0;
                }

                // 1b) Лимиты активаций способностей (Ability.MaxActivationsPerTurn) — обнуляем у карт
                //     ВЛАДЕЛЬЦА, чей ход начался. Компонент висит только на ability-сущностях с лимитом.
                foreach (var ae in _abilityLimits.Value)
                {
                    if (!_abilityOwnerPool.Value.Has(ae)) continue;
                    if (_abilityOwnerPool.Value.Get(ae).PlayerEntity != entity) continue;
                    _abilityLimitPool.Value.Get(ae).UsedThisTurn = 0;
                }

                // 2) Золото — кроме игрока с маркером GoldBlockComponent (Наступающий кризис): доход не начисляем.
                bool isLocal = _localPool.Value.Has(entity);
                if (!_goldBlockPool.Value.Has(entity))
                {
                    ref var gold = ref _goldPool.Value.Get(entity);
                    gold.Max = Mathf.Min(gold.Max + GoldIncome(start.PersonalTurnNumber), 10);
                    gold.Current = gold.Max;
                    GameEventBus.Publish(new ResourceChangedEvent
                    {
                        isLocalPlayer = isLocal,
                        Type = Service.EnumService.ResourceType.Gold,
                        NewValue = gold.Current, MaxValue = gold.Max
                    });
                }
                if (_manaPool.Value.Has(entity))
                {
                    ref var mana = ref _manaPool.Value.Get(entity);
                    // Пол маны (Вечная попойка): в начале хода поднимаем ману до Floor, если ниже. Выше — не трогаем.
                    if (_manaFloorPool.Value.Has(entity))
                    {
                        int floor = _manaFloorPool.Value.Get(entity).Floor;
                        if (mana.Max < floor) mana.Max = floor;
                        if (mana.Current < floor) mana.Current = floor;
                    }
                    GameEventBus.Publish(new ResourceChangedEvent
                    {
                        isLocalPlayer = isLocal,
                        Type = Service.EnumService.ResourceType.Mana,
                        NewValue = mana.Current, MaxValue = mana.Max
                    });
                }

                // 2b) Кулдаун командира владельца
                foreach (var ce in _commanderCd.Value)
                {
                    if (_ownerPool.Value.Get(ce).OwnerId != playerId) continue;
                    ref var cd = ref _commanderCdPool.Value.Get(ce);
                    if (cd.TurnsRemaining > 0) cd.TurnsRemaining--;
                    else
                    {
                        _commanderCdPool.Value.Del(ce);
                        GameEventBus.Publish(new CommanderCooldownExpiredUIEvent { CardEntity = ce });
                    }
                }

                // 3) Добор (turn-start → Sync=true: каскад идёт только у активного, пассив повторит по ActionDrawData)
                //    Адовый червь ЗАМЕНЯЕТ саму механику добора начала хода: вместо взятия верхней карты
                //    игрок смотрит N верхних и выбирает. Решается ЗДЕСЬ, у источника — перехватывать
                //    DrawCardEvent ниже по течению нельзя: DrawCardEffect суммирует Count в уже
                //    существующее событие, и базовый добор от эффектных там уже не отличить.
                if (_drawReplPool.Value.Has(entity))
                {
                    if (!_drawDuePool.Value.Has(entity)) _drawDuePool.Value.Add(entity);
                }
                else
                {
                    if (!_drawPool.Value.Has(entity)) _drawPool.Value.Add(entity);
                    _drawPool.Value.Get(entity).Sync = true;
                }

                // 4) Сигнал начала хода (bus): сброс индекса действий у коллектора +
                //    OnTurnStartTrigger способностей ловят TurnStartedEvent и поднимают каст.
                //    (Per-card TurnStartEvent ниже — легаси-маркер, потребителя больше нет,
                //     DelHere его чистит; оставлен инертным, не мешает.)
                foreach (var ce in _boardCards.Value)
                {
                    if (_ownerPool.Value.Get(ce).OwnerId != playerId) continue;
                    if (!_turnStartEventPool.Value.Has(ce)) _turnStartEventPool.Value.Add(ce);
                }

                // Записки о причине (CausedByActivationComponent) принадлежат каскаду, а каскад не
                // пересекает границу хода. Снимаем ДО TurnStartedEvent, иначе карта, порождённая эффектом
                // в прошлом ходу и не разыгранная, утащила бы за собой древнюю волну и встала бы в очередь
                // перед свежими активациями этого хода.
                CauseStamp.ClearAll(systems.GetWorld());

                GameEventBus.Publish(new TurnStartedEvent { ActivePlayerId = playerId, TurnNumber = start.TurnNumber });

                // Задачи: на старте СВОЕГО хода считаем свои чары (каждая = +1 к «чары × ходов»).
                if (isLocal)
                {
                    int charmCount = 0;
                    foreach (var ch in _charms.Value)
                        if (_ownerPool.Value.Get(ch).OwnerId == playerId) charmCount++;
                    if (charmCount > 0)
                        GameEventBus.Publish(new CharmsControlledTrackedEvent { OwnerId = playerId, Count = charmCount });
                }

                UnityEngine.Debug.Log($"[TurnStart] cascade for player={playerId} turn={start.TurnNumber} personal={start.PersonalTurnNumber}");
            }
        }

        // Классический доход: +1 к максимуму золота КАЖДЫЙ ход (кап 10). Разгонная кривая (+2 с 4-го,
        // +3 с 7-го хода) убрана решением юзера 2026-07-29 — «такого требования не было».
        static int GoldIncome(int personalTurnNumber) => 1;
    }
}
