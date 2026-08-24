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
        readonly EcsWorldInject _world = default;   // для пере-привязки способностей краденого командира
        readonly EcsCustomInject<BoardView> _boardView = default;
        readonly EcsFilterInject<Inc<DeadTag, BoardTag, CreatureTag>> _filter = default;

        readonly EcsPoolInject<AttackAnimPendingTag> _attackAnimPool = default;
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

        // Детерминированный порядок обработки смертей (см. Run): штамп выхода на стол + буфер пачки.
        readonly EcsPoolInject<BoardEntryOrderComponent> _entryOrderPool = default;
        readonly System.Collections.Generic.List<int> _deathBatch = new();

        int DeathEntrySeq(int entity)
            => _entryOrderPool.Value.Has(entity) ? _entryOrderPool.Value.Get(entity).Seq : 0;
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
        readonly EcsPoolInject<DoubleAttackTag>     _doubleAttackPool = default;
        readonly EcsPoolInject<ShieldComponent>     _shieldPool       = default;
        readonly EcsPoolInject<InvulnerableTag>     _invulnerablePool = default;
        readonly EcsPoolInject<TauntTag>            _tauntPool        = default;
        readonly EcsPoolInject<PoisonComponent>     _poisonPool       = default;
        readonly EcsPoolInject<StealthComponent>    _stealthPool      = default;
        readonly EcsPoolInject<VenomousComponent>   _venomousPool     = default;
        readonly EcsPoolInject<RetaliateTag>        _retaliatePool    = default;
        readonly EcsPoolInject<VampirismTag>        _vampirismPool    = default;
        readonly EcsPoolInject<CreatureTimerComponent> _creatureTimerPool = default;   // «умрёт через N ходов»

        // БАЗОВЫЙ ДОХОД МАНЫ: убил ВРАЖЕСКОЕ существо (ЛЮБЫМ способом — бой/урон-спелл через
        // TakeDamageSystem, прямое уничтожение через Destroy-эффекты; все пишут KilledByComponent) →
        // +мана убийце. Единая точка начисления. Гоняется на обоих клиентах → зеркально.
        readonly EcsPoolInject<KilledByComponent> _killedByPool = default;
        readonly EcsPoolInject<ManaComponent> _manaPool = default;
        readonly EcsFilterInject<Inc<PlayerComponent, ManaComponent>> _playerManaFilter = default;
        const int ManaPerKill = 1;   // сколько маны за убитое вражеское существо (баланс-кнопка)
        const int ManaCap     = 10;  // потолок маны (как у золота)

        // Захваченный (TakeControlEffect) командир при смерти возвращается ИЗНАЧАЛЬНОМУ владельцу, а не
        // текущему контролёру — см. ReturnCommanderToHand.
        readonly EcsPoolInject<OriginalOwnerComponent>  _originalOwnerPool = default;
        readonly EcsPoolInject<TempControlledComponent> _tempControlPool  = default;
        readonly EcsPoolInject<OwnCardTag>              _ownCardTagPool   = default;
        readonly EcsPoolInject<EnemyCardTag>            _enemyCardTagPool = default;

        // Гонка Cast/Death на ОДНОМ Animator (напр. Всадники: OnCast+SelfDestruct — существо умирает от
        // СВОЕЙ ЖЕ способности, пока играет её анимацию каста): если триггернуть "Death" ПОКА ещё крутится
        // "Cast" на том же аниматоре, контроллер может не принять переход (нет валидного Cast→Death) →
        // смерть виснет за анимацией каста и просто телепортируется (SetActive(false) по deathMaxSeconds
        // таймауту), не дав анимации смерти вообще начаться. Ждём, пока СВОЯ cast-анимация полностью
        // закончится (AbilityAnimPendingComponent снят), и только потом обрабатываем смерть.
        readonly EcsFilterInject<Inc<AbilityAnimPendingComponent, AbilityOwnerComponent>> _castingAbilities = default;
        readonly EcsPoolInject<AbilityOwnerComponent> _abilityOwnerPool = default;

        readonly EcsFilterInject<Inc<PlayerComponent, HandComponent>> _playerFilter = default;

        // Анти-софтлок гейтов смерти: существо с DeadTag не должно ждать анимаций ВЕЧНО. Если FinishEvent
        // каста/атаки потерялся (анимация прервана, вьюха пересоздана, VFX убит), HasPendingCastAnim/
        // AttackAnimPending никогда не снимутся → PlayDeath не вызывается → «вьюха-зомби» стоит на столе
        // со стат-бабблами. Дедлайн чисто вьюшечный: game-state не меняет (смерть неизбежна и уже
        // зафиксирована DeadTag'ом), поэтому расхождение МОМЕНТА обработки между клиентами безопасно.
        const float GateDeadlineSeconds = 3f;
        readonly System.Collections.Generic.Dictionary<int, float> _gateSince = new();

        // DeathAnimPendingTag (см. её докстринг): вешаем в момент PlayDeath, снимаем по колбэку на
        // РЕАЛЬНОЕ завершение (тот же FinishEvent, что гасит вьюху) — колбэк приходит из Mono в
        // произвольный момент, поэтому копим токены в очереди и снимаем компонент здесь, в Run().
        readonly EcsPoolInject<DeathAnimPendingTag> _deathAnimPool = default;
        readonly EcsFilterInject<Inc<DeathAnimPendingTag>> _deathAnimFilter = default;
        readonly System.Collections.Generic.Queue<int> _deathAnimDone = new();
        const float DeathAnimGateSeconds = 3f;   // анти-софтлок доп. слоем поверх deathMaxSeconds в CreatureView

        public void Run(IEcsSystems systems)
        {
            // DeathAnimPendingTag: снять по реальному завершению (очередь колбэков) + анти-софтлок по дедлайну.
            while (_deathAnimDone.Count > 0)
            {
                int token = _deathAnimDone.Dequeue();
                if (_deathAnimPool.Value.Has(token)) _deathAnimPool.Value.Del(token);
            }
            foreach (var e in _deathAnimFilter.Value)
                if (_deathAnimPool.Value.Get(e).Deadline <= Time.time)
                    _deathAnimPool.Value.Del(e);

            // ПОРЯДОК СМЕРТЕЙ = порядок ВЫХОДА на стол (тот же закон, что у баунса в RunLeaveBoardSystem
            // и у активаций в очереди). Порядок ecslite-фильтра произвольный (свап-удаления), а от него
            // зависит очередь хрипов: они становятся СЛЕДСТВИЯМИ одной причины и нумеруются по приходу
            // (ActivationKey.Child). Без сортировки каскад из нескольких одновременных смертей
            // резолвился бы каждый раз в новом порядке — недетерминированно для игрока и для реплея.
            _deathBatch.Clear();
            foreach (var e in _filter.Value) _deathBatch.Add(e);
            _deathBatch.Sort((a, b) => DeathEntrySeq(a).CompareTo(DeathEntrySeq(b)));

            foreach (var entity in _deathBatch)
            {
                if (!_deadPool.Value.Has(entity)) continue;   // сущность могла измениться в этой же пачке
                // Гейты анимаций (гонки Cast/Death и Attack/Death на одном Animator) — с дедлайном.
                bool gated = HasPendingCastAnim(entity) || _attackAnimPool.Value.Has(entity);
                if (gated)
                {
                    if (!_gateSince.TryGetValue(entity, out float since))
                    {
                        _gateSince[entity] = UnityEngine.Time.time;
                        continue;   // ждём конца СВОЕЙ анимации — попробуем следующим кадром
                    }
                    if (UnityEngine.Time.time - since < GateDeadlineSeconds) continue;
                    UnityEngine.Debug.LogWarning($"[Die] entity={entity} гейт смерти висит > {GateDeadlineSeconds}с (потерян Finish анимации?) → форсим обработку смерти");
                }
                _gateSince.Remove(entity);

                // Изоляция сбоев: BoardTag снимается в начале обработки — если ниже кинет ЛЮБОЙ подписчик
                // (CreatureDiedEvent → ревёрт аур/триггеры, ResourceChanged → UI), сущность выпадала из
                // фильтра смерти НАВСЕГДА с полуобработанной смертью: PlayDeath не вызван → «вьюха-зомби»
                // стоит на столе без единого лога смерти. Ловим, логируем и ДОДЕЛЫВАЕМ уборку вьюхи.
                try
                {
                    ProcessDeath(entity);
                }
                catch (System.Exception ex)
                {
                    UnityEngine.Debug.LogError($"[Die] entity={entity} исключение при обработке смерти: {ex}");
                    if (!_gravePool.Value.Has(entity) && !_limboPool.Value.Has(entity) && !_handTagPool.Value.Has(entity))
                        SendToGrave(entity);   // минимум: карта в грав + PlayDeath, чтобы вьюха не зависла
                }
            }
        }

        void ProcessDeath(int entity)
        {
            {
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

                // Убираем цель из трекинга ЛЮБЫХ Tracked-аур (см. TrackedBuffs.RemoveTarget) — иначе для
                // командира (та же ecs-сущность после смерти) идемпотентность ауры навсегда блокирует
                // повторный бафф при повторном розыгрыше. ДВА параллельных механизма трекинга — снимаем
                // из ОБОИХ (AppliedBuffs — тот же повод, для ApplyTrackedBuffEffect).
                TrackedBuffs.RemoveTarget(_world.Value, entity);
                AppliedBuffs.RemoveTarget(_world.Value, entity);

                // Существо было ИСТОЧНИКОМ ауро-модификатора стоимости (AddCostAuraEffect, напр. Носитель
                // кодила) — снимаем его записи со всех игроков, иначе налог остаётся навсегда после смерти.
                AuraCostModifiers.RemoveBySource(_world.Value, entity);

                bool isCommander = _commanderPool.Value.Has(entity);
                bool isToken = _tokenPool.Value.Has(entity);

                int ownerId = _ownerPool.Value.Has(entity) ? _ownerPool.Value.Get(entity).OwnerId : -1;

                // Доход маны за килл: атрибуция стоит (KilledBy) и убийца — ВРАГ владельца жертвы.
                if (_killedByPool.Value.Has(entity))
                {
                    int killer = _killedByPool.Value.Get(entity).PlayerId;
                    _killedByPool.Value.Del(entity);   // одноразово (возврат из кладбища не переначислит)
                    if (killer >= 0 && killer != ownerId)
                        AwardManaForKill(killer);
                }

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

        // Начисляет ману игроку-убийце (поднимает Max и Current, кап ManaCap — как у золота). Публикует
        // ResourceChangedEvent для UI. Гоняется на обоих клиентах → значения сходятся без отдельного синка.
        void AwardManaForKill(int playerId)
        {
            foreach (var pe in _playerManaFilter.Value)
            {
                ref var p = ref _playerPool.Value.Get(pe);
                if (p.PlayerId != playerId) continue;

                ref var mana = ref _manaPool.Value.Get(pe);
                mana.Max     = System.Math.Min(mana.Max + ManaPerKill, ManaCap);
                mana.Current = System.Math.Min(mana.Current + ManaPerKill, mana.Max);

                GameEventBus.Publish(new ResourceChangedEvent
                {
                    isLocalPlayer = p.IsLocalPlayer,
                    Type          = Game.Core.Service.EnumService.ResourceType.Mana,
                    NewValue      = mana.Current,
                    MaxValue      = mana.Max,
                });
                return;
            }
        }

        private void SendToGrave(int entity)
        {
            _gravePool.Value.Add(entity);

            if (_viewPool.Value.Has(entity))
            {
                ref var vr = ref _viewPool.Value.Get(entity);
                var view = vr.View != null ? vr.View.GetComponent<CreatureView>() : null;
                if (view != null) PlayDeathGated(view, entity);
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
                var view = vr.View != null ? vr.View.GetComponent<CreatureView>() : null;
                if (view != null) PlayDeathGated(view, entity);
            }

            _limboPool.Value.Add(entity);
        }

        // Вешает DeathAnimPendingTag ДО запуска анимации и снимает его по РЕАЛЬНОМУ завершению (тот же
        // FinishEvent/таймаут-фолбэк, что гасит вьюху) — см. докстринг DeathAnimPendingTag.
        void PlayDeathGated(CreatureView view, int entity)
        {
            ref var pending = ref _deathAnimPool.Value.Add(entity);
            pending.Deadline = Time.time + DeathAnimGateSeconds;
            int token = entity;
            view.PlayDeath(() => _deathAnimDone.Enqueue(token));
        }

        private void ReturnCommanderToHand(int entity, int ownerId)
        {
            // Мировая позиция ДО зачистки ниже (BoardPositionComponent снимается чуть дальше) — UI летит
            // командиром в руку с его собственной клетки, а не с дефолтной точки «из-за края экрана».
            Vector3? fromWorld = null;
            if (EntityWorldPosUtil.TryGet(_world.Value, _boardView.Value, entity, out var srcPos))
                fromWorld = srcPos;

            // Захваченный командир (TakeControlEffect: контроль-на-месте, OwnerId сменился на захватчика) при
            // смерти возвращается ИЗНАЧАЛЬНОМУ владельцу, а не текущему контролёру — иначе игрок, укравший
            // командира, получал бы его в СВОЮ руку. OriginalOwnerComponent ставится ОДИН раз при первом
            // захвате (переживает и Temporary, и постоянный TakeControlEffect) — если её нет, командира никто
            // не захватывал, ownerId уже правильный.
            if (_originalOwnerPool.Value.Has(entity))
            {
                int originalOwnerId = _originalOwnerPool.Value.Get(entity).OwnerId;
                if (originalOwnerId != ownerId)
                {
                    ownerId = originalOwnerId;
                    RevertOwnershipToOriginal(entity, originalOwnerId);
                    // ХС-семантика: способности при краже были пере-привязаны к вору (IAbility.Rebind) —
                    // возвращаем командира истинному владельцу ВМЕСТЕ с его способностями, иначе при
                    // повторном розыгрыше из руки эффекты кредитовали бы похитителя.
                    AbilityRebindUtil.RebindToPlayerId(_world.Value, entity, originalOwnerId);
                }
                _originalOwnerPool.Value.Del(entity);   // командир снова у истинного владельца — память не нужна
            }
            // Временный контроль (TempControlledComponent) больше не актуален — командир покидает борд.
            if (_tempControlPool.Value.Has(entity))
                _tempControlPool.Value.Del(entity);

            // Визуал: сначала АНИМАЦИЯ СМЕРТИ (как у обычных существ — раньше инстанс сносился мгновенно
            // и командир «телепортировался» в руку), уничтожение — отложенно, когда анимация точно
            // отыграла (PlayDeath сам прячет вьюшку по её концу; 3с > deathMaxSeconds-фолбэка). Ссылку
            // обнуляем СРАЗУ — повторное развёртывание создаст новый инстанс, не трогая доигрывающий.
            if (_viewPool.Value.Has(entity))
            {
                ref var vr = ref _viewPool.Value.Get(entity);
                if (vr.View != null)
                {
                    var view = vr.View.GetComponent<CreatureView>();
                    if (view != null) PlayDeathGated(view, entity);
                    Object.Destroy(vr.View, 3f);
                }
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
                GameEventBus.Publish(new CardDrawnEvent { CardEntity = entity, PlayerId = playerEntity, FromWorld = fromWorld });
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

            // Свойства (Двойной удар/Укреплённый) — сняты со смертью, как любой другой рантайм-модификатор.
            // Печатные (CardCreatureModel.Properties) переживут это молча: возрождение (SummonSelfEffect и
            // т.п.) заново гонит Init → Properties.Apply навесит их обратно с чистого листа.
            if (_doubleAttackPool.Value.Has(entity)) _doubleAttackPool.Value.Del(entity);
            if (_shieldPool.Value.Has(entity))
            {
                _shieldPool.Value.Del(entity);
                GameEventBus.Publish(new CreaturePropertyAuraChangedEvent { CreatureEntity = entity, Key = "Shielded", Active = false });
            }
            if (_invulnerablePool.Value.Has(entity)) _invulnerablePool.Value.Del(entity);
            if (_tauntPool.Value.Has(entity))
            {
                _tauntPool.Value.Del(entity);
                GameEventBus.Publish(new CreaturePropertyAuraChangedEvent { CreatureEntity = entity, Key = "Taunt", Active = false });
            }
            if (_poisonPool.Value.Has(entity))
            {
                _poisonPool.Value.Del(entity);
                GameEventBus.Publish(new CreaturePropertyAuraChangedEvent { CreatureEntity = entity, Key = "Poisoned", Active = false });
            }
            if (_stealthPool.Value.Has(entity))      _stealthPool.Value.Del(entity);
            if (_venomousPool.Value.Has(entity))      _venomousPool.Value.Del(entity);
            if (_retaliatePool.Value.Has(entity))     _retaliatePool.Value.Del(entity);
            if (_vampirismPool.Value.Has(entity))     _vampirismPool.Value.Del(entity);

            // «Умрёт через N ходов» (BuffDeathTimer/AddCreatureTimerEffectComponent) — снимаем со смертью,
            // как любой другой рантайм-модификатор. Без этого протухший таймер (уже ≤0) переживает смерть
            // молча (CreatureTimerTickSystem не видит его без BoardTag), а для КОМАНДИРА — единственной
            // карты, что возвращается на стол ТОЙ ЖЕ сущностью после смерти (кулдаун в руке, не кладбище) —
            // тут же добивает его на первом ходу владельца после повторного розыгрыша, и так бесконечно
            // по кругу (баг 2026-08-11: «Лесная милфа» вечно умирала после первого же вражеского дебаффа).
            if (_creatureTimerPool.Value.Has(entity)) _creatureTimerPool.Value.Del(entity);
        }

        private int FindPlayerEntity(int playerId)
        {
            foreach (var pe in _playerFilter.Value)
                if (_playerPool.Value.Get(pe).PlayerId == playerId) return pe;
            return -1;
        }

        // Откатывает OwnerComponent + клиент-относительные Own/EnemyCardTag на ИЗНАЧАЛЬНОГО владельца
        // (симметрично TempControlRevertSystem.RevertControl, но здесь считаем «свой/чужой» напрямую по
        // PlayerComponent.IsLocalPlayer — OriginalWasOwn тут не хранится, в отличие от TempControlledComponent).
        private void RevertOwnershipToOriginal(int entity, int originalOwnerId)
        {
            if (_ownerPool.Value.Has(entity))
                _ownerPool.Value.Get(entity).OwnerId = originalOwnerId;

            int localPlayerId = -1;
            foreach (var pe in _playerFilter.Value)
                if (_playerPool.Value.Get(pe).IsLocalPlayer) { localPlayerId = _playerPool.Value.Get(pe).PlayerId; break; }

            bool isOwn = originalOwnerId == localPlayerId;
            if (isOwn)
            {
                if (_enemyCardTagPool.Value.Has(entity)) _enemyCardTagPool.Value.Del(entity);
                if (!_ownCardTagPool.Value.Has(entity))   _ownCardTagPool.Value.Add(entity);
            }
            else
            {
                if (_ownCardTagPool.Value.Has(entity))    _ownCardTagPool.Value.Del(entity);
                if (!_enemyCardTagPool.Value.Has(entity)) _enemyCardTagPool.Value.Add(entity);
            }
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
