using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Mono;
using UnityEngine;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Обрабатывает существ с DeadTag:
    ///   1. Добавляет DieEvent (→ OnDie-триггеры запустят предсмертные способности).
    ///   2. Обычные существа: снимает BoardTag, добавляет GraveTag, играет анимацию смерти.
    ///   3. Командир: НЕ уходит на кладбище — возвращается в руку с кулдауном 1 ход
    ///      (визуал деспавнится, чтобы корректно развернуться повторно).
    ///   4. Публикует DeathTrackedEvent (трекинг матча).
    /// </summary>
    public sealed class DieSystem : IEcsRunSystem
    {
        readonly EcsFilterInject<Inc<DeadTag, BoardTag, CreatureTag>> _filter = default;

        readonly EcsPoolInject<BoardTag> _boardPool = default;
        readonly EcsPoolInject<GraveTag> _gravePool = default;
        readonly EcsPoolInject<DieEvent> _diePool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;
        readonly EcsPoolInject<SelectTag> _selectPool = default;
        readonly EcsPoolInject<ViewRefComponent> _viewPool = default;
        readonly EcsPoolInject<CardModelComponent> _modelPool = default;

        // Командир
        readonly EcsPoolInject<CommanderTag> _commanderPool = default;
        readonly EcsPoolInject<HandTag> _handTagPool = default;
        readonly EcsPoolInject<HandComponent> _handPool = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsPoolInject<DeadTag> _deadPool = default;
        readonly EcsPoolInject<TokenTag> _tokenPool = default;
        readonly EcsPoolInject<LimboTag> _limboPool = default;
        readonly EcsPoolInject<BoardPositionComponent> _posPool = default;
        readonly EcsPoolInject<ViewSpawnedTag> _spawnedPool = default;
        readonly EcsPoolInject<HealthComponent> _hpPool = default;
        readonly EcsPoolInject<SpeedComponent> _speedPool = default;
        readonly EcsPoolInject<AttackComponent> _atkPool = default;
        readonly EcsPoolInject<AttacksUsedComponent> _attacksUsedPool = default;
        readonly EcsPoolInject<GoldCostComponent>   _goldCostPool   = default;
        readonly EcsPoolInject<ManaCostComponent>   _manaCostPool   = default;
        readonly EcsPoolInject<HealthCostComponent> _healthCostPool = default;

        // Гонка Cast/Death на ОДНОМ Animator (напр. Всадники: OnCast+SelfDestruct — существо умирает от
        // СВОЕЙ ЖЕ способности, пока играет её анимацию каста): если триггернуть "Death" ПОКА ещё крутится
        // "Cast" на том же аниматоре, контроллер может не принять переход (нет валидного Cast→Death) →
        // смерть виснет за анимацией каста и просто телепортируется (SetActive(false) по deathMaxSeconds
        // таймауту), не дав анимации смерти вообще начаться. Ждём, пока СВОЯ cast-анимация полностью
        // закончится (AbilityAnimPendingComponent снят), и только потом обрабатываем смерть.
        readonly EcsFilterInject<Inc<AbilityAnimPendingComponent, AbilityOwnerComponent>> _castingAbilities = default;
        readonly EcsPoolInject<AbilityOwnerComponent> _abilityOwnerPool = default;

        readonly EcsFilterInject<Inc<PlayerComponent, HandComponent>> _playerFilter = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var entity in _filter.Value)
            {
                if (HasPendingCastAnim(entity)) continue;   // ждём конца СВОЕЙ анимации каста — попробуем следующим кадром

                if (_selectPool.Value.Has(entity))
                    _selectPool.Value.Del(entity);

                _boardPool.Value.Del(entity);

                // Предсмертные способности срабатывают в обоих случаях
                if (!_diePool.Value.Has(entity))
                    _diePool.Value.Add(entity);

                // Шина: OnDie/OnAllyDied-триггеры и авто-ревёрт аур слушают CreatureDiedEvent. Публикуем на
                // обоих клиентах (смерть синкается) — триггеры гейтятся AbilityFire (актив), ревёрт ауры
                // зеркальный. KillerEntity=-1 (атрибуция убийцы — отдельный TODO, нужно для OnKill).
                GameEventBus.Publish(new CreatureDiedEvent { CardEntity = entity, KillerEntity = -1 });

                // При смерти чистим МЯГКИЕ стат-модификаторы (ауры/«мягкий перм»); перманентные остаются.
                ClearStatModifiers(entity);

                bool isCommander = _commanderPool.Value.Has(entity);
                bool isToken = _tokenPool.Value.Has(entity);

                int ownerId = _ownerPool.Value.Has(entity) ? _ownerPool.Value.Get(entity).OwnerId : -1;

                if (isCommander)
                    ReturnCommanderToHand(entity, ownerId);
                else if (isToken)
                    SendToLimbo(entity);
                else
                    SendToGrave(entity);

                // Уведомляем MatchTracker
                string cardName = string.Empty;
                int modelId = -1;
                if (_modelPool.Value.Has(entity))
                {
                    ref var m = ref _modelPool.Value.Get(entity);
                    cardName = m.CardName;
                    modelId  = m.ModelId;
                }
                GameEventBus.Publish(new DeathTrackedEvent
                {
                    CreatureEntity = entity,
                    OwnerId        = ownerId,
                    KillerPlayerId = -1,   // TODO: передавать через AttackHitEvent если нужно
                    CardName       = cardName,
                    ModelId        = modelId,
                });

                GameEventBus.Publish(new InputBlockedEvent());
            }
        }

        private void SendToGrave(int entity)
        {
            _gravePool.Value.Add(entity);

            if (_viewPool.Value.Has(entity))
            {
                ref var vr = ref _viewPool.Value.Get(entity);
                vr.View?.GetComponent<CreatureView>()?.PlayDeath();
            }
        }

        // Токен не идёт на кладбище — уходит в Limbo (вне геймплея). Визуал — как смерть (анимация/ужатие),
        // а НЕ мгновенное уничтожение (баг #1: токены-«всадники» просто исчезали). Вьюшку не уничтожаем —
        // PlayDeath сам её выключит после анимации.
        private void SendToLimbo(int entity)
        {
            if (_viewPool.Value.Has(entity))
            {
                ref var vr = ref _viewPool.Value.Get(entity);
                vr.View?.GetComponent<CreatureView>()?.PlayDeath();
            }

            _limboPool.Value.Add(entity);
        }

        private void ReturnCommanderToHand(int entity, int ownerId)
        {
            // Деспавним визуал, чтобы повторное развёртывание создало новый инстанс
            if (_viewPool.Value.Has(entity))
            {
                ref var vr = ref _viewPool.Value.Get(entity);
                if (vr.View != null)
                    Object.Destroy(vr.View);
                vr.View = null;
            }
            if (_spawnedPool.Value.Has(entity))
                _spawnedPool.Value.Del(entity);

            if (_posPool.Value.Has(entity))
                _posPool.Value.Del(entity);

            // Снимаем «мёртвость» — командир жив, но недоступен (кулдаун)
            if (_deadPool.Value.Has(entity))
                _deadPool.Value.Del(entity);

            // Статы уже сброшены (ClearStatModifiers снял мягкие модификаторы → Max пересчитан на
            // Base+перманентные). Лечим до полного и восстанавливаем бюджет действий.
            if (_hpPool.Value.Has(entity))
            {
                ref var hp = ref _hpPool.Value.Get(entity);
                hp.Current = hp.Max;
            }
            if (_speedPool.Value.Has(entity))
            {
                ref var sp = ref _speedPool.Value.Get(entity);
                sp.Remaining = sp.Max;
            }
            // Счётчик атак за ход: сбрасывается на СТАРТЕ хода только у существ НА БОРДЕ — командир в этот
            // момент в руке, и залипшее значение с прошлой жизни блокировало атаку в ход повторного розыгрыша.
            if (_attacksUsedPool.Value.Has(entity))
                _attacksUsedPool.Value.Get(entity).Value = 0;

            // Возвращаем в руку (для повторного розыгрыша) и ставим кулдаун
            if (!_handTagPool.Value.Has(entity))
                _handTagPool.Value.Add(entity);

            int playerEntity = FindPlayerEntity(ownerId);
            if (playerEntity >= 0)
            {
                ref var hand = ref _handPool.Value.Get(playerEntity);
                if (hand.CardEntities != null && !hand.CardEntities.Contains(entity))
                {
                    hand.CardEntities.Insert(0, entity);
                    hand.Count = hand.CardEntities.Count;
                }

                // Вернуть карту командира в руку-UI: HandUISystem ловит CardDrawnEvent (только для локального
                // игрока) → CardAddedToHandUIEvent → CardLayout снова покажет карту. Без этого карта в руке
                // логически есть, но визуально не появляется.
                // ВАЖНО: публикуем ВСЕГДА (вне Contains-проверки выше). У командира выделенный слот
                // (_commanderSlot), «места» для него хватает всегда. Если на повторной смерти он логически
                // уже числился в hand.CardEntities (re-cast не снял из списка) — раньше событие не слалось и
                // командир переставал визуально возвращаться (баг 2-й смерти).
                GameEventBus.Publish(new CardDrawnEvent { CardEntity = entity, PlayerId = playerEntity });
            }

            // Кулдаун НЕ ставим здесь: его вешает RunCommanderCooldownSystem на сам факт возврата в руку
            // (переход по HandTag) — так кулдаун срабатывает на ЛЮБОЙ возврат (смерть/баунс/«вернуть в руку»),
            // а не только на смерть.
        }

        // Чистка МЯГКИХ стат-модификаторов при смерти (перманентные остаются). Пересчёт внутри ClearModifiers.
        // Стоимость — тот же пайплайн (мягкие кост-баффы сгорают, перманентная скидка/наценка переживает смерть).
        private void ClearStatModifiers(int entity)
        {
            if (_atkPool.Value.Has(entity))   { ref var a = ref _atkPool.Value.Get(entity);   a.ClearModifiers(); }
            if (_hpPool.Value.Has(entity))    { ref var h = ref _hpPool.Value.Get(entity);     h.ClearModifiers(); }
            if (_speedPool.Value.Has(entity)) { ref var s = ref _speedPool.Value.Get(entity);  s.ClearModifiers(); }
            if (_goldCostPool.Value.Has(entity))   { ref var c = ref _goldCostPool.Value.Get(entity);   c.ClearModifiers(); }
            if (_manaCostPool.Value.Has(entity))   { ref var c = ref _manaCostPool.Value.Get(entity);   c.ClearModifiers(); }
            if (_healthCostPool.Value.Has(entity)) { ref var c = ref _healthCostPool.Value.Get(entity); c.ClearModifiers(); }
        }

        private int FindPlayerEntity(int playerId)
        {
            foreach (var pe in _playerFilter.Value)
                if (_playerPool.Value.Get(pe).PlayerId == playerId) return pe;
            return -1;
        }

        // Существо САМО ещё играет анимацию каста (AbilityAnimPendingComponent на его же способности) —
        // напр. Всадники: SelfDestruct резолвится ПОСЛЕ CastEvent, но гейт снимается только на FinishEvent
        // (см. RunResolveAbilityQueueSystem). Ждём FinishEvent (или анти-софтлок таймаут) ПЕРЕД тем, как
        // триггерить "Death" на том же аниматоре — иначе анимация смерти может не начаться вовсе.
        private bool HasPendingCastAnim(int cardEntity)
        {
            foreach (var ae in _castingAbilities.Value)
                if (_abilityOwnerPool.Value.Get(ae).CardEntity == cardEntity) return true;
            return false;
        }
    }
}
