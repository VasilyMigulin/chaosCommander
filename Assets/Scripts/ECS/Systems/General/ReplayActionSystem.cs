using System.Collections.Generic;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Mono;
using Game.Core.Network;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using UnityEngine;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// Пассивный клиент: интерпретирует снапшоты оппонента из ActionQueue. Работает ТОЛЬКО когда
    /// локальный игрок пассивен (чужой ход — у него нет TurnState). Берёт ПО ОДНОМУ действию за кадр
    /// и НЕ берёт следующее, пока идёт анимация (MovingTag/AttackAnimPendingTag) — как у активного.
    ///
    /// Триггеры/правила/таргетинг на пассиве не работают (см. TurnGate в AbilityFire). Размещение
    /// карт НЕ публикует InvokeEvent/CardCastEvent (триггеров нет); эффекты способностей придут
    /// отдельными ActionAbilityData (P2).
    /// </summary>
    public sealed class ReplayActionSystem : IEcsRunSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsSharedInject<IGameStateContext> _state = default;

        readonly EcsPoolInject<CreatureTag> _creatureTag = default;
        readonly EcsPoolInject<SpellTag>    _spellTag    = default;
        readonly EcsPoolInject<CharmTag>    _charmTag    = default;
        readonly EcsPoolInject<OwnerComponent>  _ownerPool  = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsPoolInject<OwnCardTag>      _ownCardPool = default;
        readonly EcsPoolInject<SpeedComponent>  _speedPool   = default;
        readonly EcsPoolInject<AbilityContainerComponent> _abilityContainerPool = default;
        readonly EcsPoolInject<AbilityQueuedState> _queuedPool = default;
        readonly EcsFilterInject<Inc<PlayerComponent>> _playerFilter = default;
        readonly EcsFilterInject<Inc<PlayerComponent, LocalComponent>> _localPlayerFilter = default;

        readonly EcsPoolInject<MoveCardToBoardEvent> _moveBoardPool = default;
        readonly EcsPoolInject<MoveCardToGraveEvent> _moveGravePool = default;
        readonly EcsPoolInject<MoveRequestEvent>     _movePool      = default;
        readonly EcsPoolInject<AttackRequestEvent>   _attackPool    = default;
        readonly EcsPoolInject<DeadTag>              _deadPool      = default;
        readonly EcsPoolInject<AbilityEffectContainerComponent> _abilityEffectPool = default;
        readonly EcsPoolInject<AbilityChainComponent> _abilityChainPool = default;
        readonly EcsPoolInject<AbilityVfxComponent> _abilityVfxPool = default;
        readonly EcsCustomInject<BoardView> _boardView = default;
        readonly EcsPoolInject<DeckComponent>  _deckPool     = default;
        readonly EcsPoolInject<HandComponent>  _handPool     = default;
        readonly EcsPoolInject<DeckTag>        _deckTagPool  = default;
        readonly EcsPoolInject<HandTag>        _handTagPool  = default;
        readonly EcsPoolInject<GraveTag>       _graveTagPool = default;
        readonly EcsPoolInject<DrawCardEvent>  _drawCardPool = default;

        readonly EcsFilterInject<Inc<MovingTag>> _movingFilter = default;
        readonly EcsFilterInject<Inc<AttackAnimPendingTag>> _animFilter = default;
        readonly EcsFilterInject<Inc<AbilityCastPendingComponent>> _castPendingFilter = default;

        float _nextActionAt;   // не берём следующее действие из очереди раньше этого времени (ActionPacing)

        // Отложенные модификаторы призыва: применяем к призванным сущностям, когда те появятся
        // (родительский ActionAbilityData приходит раньше ActionCastData призванного).
        readonly List<PendingSummonMod> _pendingMods = new List<PendingSummonMod>();

        sealed class PendingSummonMod
        {
            public int Caster;
            public IEffect[] Mods;
            public List<string> Keys;
            public int Ttl;
        }

        public void Run(IEcsSystems systems)
        {
            // Отложенные модификаторы призыва тикаем всегда (призванная сущность могла ещё не появиться).
            ProcessPendingSummonMods();

            // Реплей только на пассиве (чужой ход).
            if (TurnGate.IsLocalActive(_world.Value)) return;

            // Гейт по анимации: следующее действие — только когда движение/атака доиграли.
            if (_movingFilter.Value.GetEntitiesCount() > 0) return;
            if (_animFilter.Value.GetEntitiesCount() > 0) return;
            if (_castPendingFilter.Value.GetEntitiesCount() > 0) return;   // снаряд в полёте — ждём прилёта

            // Базовая пауза между реплеем соседних действий — читаемость (не «мешанина» за 1-2 кадра).
            if (Time.time < _nextActionAt) return;

            if (!ActionQueue.TryDequeue(out IActionData action)) return;   // одно за кадр, по порядку

            // NB: защиту от эха даёт ТРАНСПОРТ — RPC_SendActionSnapshot объявлен с InvokeLocal=false, поэтому
            // отправитель НЕ получает свой же снапшот. Значит в очереди только действия ОППОНЕНТА — реплеим всё.
            // РАНЬШЕ тут стоял гард IsOwnSource (скип, если источник — наша карта по OwnCardTag). При InvokeLocal=
            // false он не ловил реальное эхо (его и нет), зато ОШИБОЧНО выбрасывал действия, которые оппонент-
            // симулятор прислал по НАШЕЙ карте: реакции на его ходу (Скелетон OnDie→в руку при смерти на ходу
            // врага) и вражеские ауры по нашим картам. Из-за него Скелетон не возвращался. Гард удалён.
            Debug.Log($"[Replay] dequeue {action?.GetType().Name} turn={action?.TurnNumber} idx={action?.ActionIndex} (left={ActionQueue.Count})");

            // Действие уже извлечено из очереди — даже при исключении оно не повторится; ловим, чтобы
            // битый снапшот не прервал остальные системы кадра.
            try
            {
                switch (action)
                {
                    case ActionCastData      cast:    ReplayCast(cast);       break;
                    case ActionMoveData      move:    ReplayMove(move);       break;
                    case ActionAttackData    attack:  ReplayAttack(attack);   break;
                    case ActionCardPickedData pick:   ReplayPicked(pick);     break;
                    case ActionAbilityData   ability: ReplayAbility(ability); break;
                    case ActionDeathData     death:   ReplayDeath(death);     break;
                    case ActionDrawReplacementData dr: ReplayDrawReplacement(dr); break;
                    case ActionDrawData      draw:    ReplayDraw(draw);       break;
                    case ActionControlRevertData cr:  ReplayControlRevert(cr); break;
                    case ActionEndTurnData   endTurn: ReplayEndTurn(endTurn); break;
                    default:
                        Debug.LogWarning($"[Replay] not handled yet: {action?.GetType().Name}");
                        break;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Replay] failed to process {action?.GetType().Name}: {e}");
            }

            _nextActionAt = Time.time + ActionPacing.GapSeconds;   // пауза перед следующим действием реплея
        }

        // ── Handlers ──────────────────────────────────────────────────────────

        private void ReplayCast(ActionCastData s)
        {
            if (!_state.Value.TryGetEntity(s.SourceEntityKey, out int card))
            {
                Debug.LogError($"[ReplayActionSystem] Cast: card not found '{s.SourceEntityKey}' — desync");
                return;
            }

            // АЛЬТЕРНАТИВНАЯ УПЛАТА (Букмекер и семейство): актив заплатил не ресурсом — зеркалим:
            // тратим заряд маркера (его поставил ре-ран инсталл-эффекта) и повторяем уплату: урон
            // владельцу (DamageSelf) или те же жертвы по ключам (discard/sacrifice/mill).
            // -1 = обычная оплата (ресурсы оппонента пассив не симулирует).
            if (s.AltPaidKind >= 0)
            {
                int altPlayer = -1;
                var pp = _world.Value.GetPool<PlayerComponent>();
                int cardOwnerId = OwnerId(card);
                foreach (var pe in _world.Value.Filter<PlayerComponent>().End())
                    if (pp.Get(pe).PlayerId == cardOwnerId) { altPlayer = pe; break; }
                if (altPlayer >= 0)
                {
                    if (_world.Value.GetPool<AltCostComponent>().Has(altPlayer))
                        AltCostUtil.ConsumeCharge(_world.Value, altPlayer);

                    var kind = (AltCostKind)s.AltPaidKind;
                    if (kind == AltCostKind.DamageSelf)
                    {
                        AltCostUtil.DamageSelf(_world.Value, altPlayer, card, s.AltPaidAmount);
                    }
                    else if (s.AltPaidKeys != null)
                    {
                        foreach (var key in s.AltPaidKeys)
                        {
                            if (!_state.Value.TryGetEntity(key, out int victim)) continue;
                            switch (kind)
                            {
                                case AltCostKind.DiscardHand:       AltCostUtil.Discard(_world.Value, victim);   break;
                                case AltCostKind.SacrificeCreature: AltCostUtil.Sacrifice(_world.Value, victim); break;
                                case AltCostKind.MillDeck:          AltCostUtil.Mill(_world.Value, victim);      break;
                            }
                        }
                    }
                }
            }

            // Размещаем БЕЗ InvokeEvent/CardCastEvent (триггеров на пассиве нет; эффекты — из ActionAbilityData).
            if (_creatureTag.Value.Has(card))
            {
                Debug.Log($"[Replay] Cast creature: card={card} cell={s.Cell} -> ({s.Cell / 5},{s.Cell % 5})");
                if (!_moveBoardPool.Value.Has(card))
                {
                    ref var m = ref _moveBoardPool.Value.Add(card);
                    m.Row = s.Cell / 5; m.Col = s.Cell % 5; m.OwnerId = OwnerId(card);
                }
                // Трекинг «выхода на стол» у пассива (для счётчиков, напр. Грыз). Ауры/«на выходе» гейтятся
                // AbilityFire (пассив пассивен) → не сработают; счётчик InvokedByArchetype зеркалится.
                GameEventBus.Publish(new CreatureInvokedEvent { CardEntity = card, OwnerId = OwnerId(card) });
            }
            else if (_spellTag.Value.Has(card))
            {
                Debug.Log($"[Replay] Cast spell: card={card} -> grave");
                if (!_moveGravePool.Value.Has(card)) _moveGravePool.Value.Add(card);
            }
            else if (_charmTag.Value.Has(card))
            {
                Debug.Log($"[Replay] Cast charm: card={card} -> board");
                if (!_moveBoardPool.Value.Has(card))
                {
                    ref var m = ref _moveBoardPool.Value.Add(card);
                    m.Row = -1; m.Col = -1; m.OwnerId = -1;
                }
            }
            else
            {
                // Нет тег-типа (напр. токен без SpellTag): каст ВСЁ РАВНО означает уход из руки → в грейв,
                // иначе карта осталась бы фантомом в зеркале руки оппонента.
                Debug.LogWarning($"[Replay] Cast: card {card} has no type tag → moving to grave (leave hand)");
                if (!_moveGravePool.Value.Has(card)) _moveGravePool.Value.Add(card);
            }

            // ТРЕКИНГ розыгрыша у ПАССИВА: CardCastEvent здесь подавлен (триггеры реплеятся отдельно), но
            // СЧЁТЧИКИ должны быть зеркальны (иначе RepeatEffect{MatchPlayed*} даст разный N у актива/пассива →
            // рассинхрон). CardPlayedEvent слушают ТОЛЬКО трекеры (не триггеры/коллектор) → безопасно.
            GameEventBus.Publish(new CardPlayedEvent { CardEntity = card, PlayerId = OwnerId(card) });

            // UI: «что разыграл оппонент» — выкатывающаяся карта слева. ReplayCast = розыгрыш карты оппонента
            // у пассива; событие иначе никто не публиковал (объявлено, но без паблишера).
            var mp = _world.Value.GetPool<CardModelComponent>();
            var vp = _world.Value.GetPool<CardViewDataComponent>();
            string cName = mp.Has(card) ? mp.Get(card).CardName : "";
            UnityEngine.Sprite cIcon = vp.Has(card) ? vp.Get(card).ArtImage : null;

            // Полные визуальные данные (как в HandUISystem) — выкат рендерит настоящую карту через CardBaseView.
            var visual = vp.Has(card) ? Game.Core.Instance.Card.CardVisualDataFactory.From(in vp.Get(card)) : default;

            GameEventBus.Publish(new OpponentCardPlayedUIEvent
            {
                CardName      = cName,
                Icon          = cIcon,
                Visual        = visual,
                // Реплей идёт в ЧУЖОЙ ход, но карта может быть НАШЕЙ: симулятор форс-кастует зеркало
                // нашей карты (Попойка с добора в его ход) и присылает каст нам же на реплей.
                IsLocalPlayer = _ownCardPool.Value.Has(card),
            });
        }

        private void ReplayMove(ActionMoveData s)
        {
            if (!_state.Value.TryGetEntity(s.SourceEntityKey, out int entity))
            {
                Debug.LogError($"[Replay] Move: entity not found '{s.SourceEntityKey}'");
                return;
            }
            if (_movePool.Value.Has(entity)) return;

            EnsureCanAct(entity);   // реплей авторитетен: скорость врага на пассиве не восстанавливается → обходим её проверку

            ref var m = ref _movePool.Value.Add(entity);
            m.ToRow = s.TargetCell / 5;
            m.ToCol = s.TargetCell % 5;
            m.ToOwnerId = s.TargetOwnerId;   // сторона доски (для ходов через фронт)
            Debug.Log($"[Replay] Move: entity={entity} -> ({m.ToRow},{m.ToCol},owner{m.ToOwnerId})");
        }

        private void ReplayAttack(ActionAttackData s)
        {
            if (!_state.Value.TryGetEntity(s.AttackerEntityKey, out int attacker))
            {
                Debug.LogError($"[Replay] Attack: attacker not found '{s.AttackerEntityKey}'");
                return;
            }
            if (string.IsNullOrEmpty(s.DefenderEntityKey)) return;
            int defender = ResolveTargetKey(s.DefenderEntityKey);   // "PLAYER:{id}" → аватар-игрок
            if (defender < 0)
            {
                Debug.LogError($"[Replay] Attack: defender not found '{s.DefenderEntityKey}'");
                return;
            }
            if (_attackPool.Value.Has(attacker)) return;

            EnsureCanAct(attacker);   // см. ReplayMove: обходим локальную проверку скорости при реплее

            ref var a = ref _attackPool.Value.Add(attacker);
            a.TargetEntity = defender;
            Debug.Log($"[Replay] Attack: attacker={attacker} -> defender={defender}");
        }

        /// <summary>
        /// Гарантирует, что у существа есть «заряд» для реплейнутого действия. На пассивном клиенте
        /// скорость вражеских существ не восстанавливается в начале их хода (каскад идёт только у активного),
        /// а MoveSystem/AttackSystem отбрасывают действие при speed.Remaining<=0. Реплей авторитетен —
        /// активный клиент уже проверил легальность, поэтому просто восстанавливаем заряд.
        /// </summary>
        private void EnsureCanAct(int entity)
        {
            if (!_speedPool.Value.Has(entity)) return;
            ref var sp = ref _speedPool.Value.Get(entity);
            if (sp.Remaining <= 0) sp.Remaining = sp.Max > 0 ? sp.Max : 1;
        }

        private void ReplayPicked(ActionCardPickedData s)
        {
            Debug.Log($"[Replay] Picked: source='{s.SourceEntityKey}' chosen='{s.ChosenEntityKey}' fromPool={s.CreateFromPool}");
            CardPickReplayStore.Set(s.SourceEntityKey, new CardPickReplayStore.PickChoice
            {
                ChosenEntityKey = s.ChosenEntityKey,
                CreateFromPool  = s.CreateFromPool,
                ExpansionId     = s.ChosenExpansionId,
                CardId          = s.ChosenCardId,
            });
        }

        /// <summary>
        /// Применение способности у пассивного: находим карту и ability-сущность по индексу,
        /// разрешаем ключи целей в локальные сущности и ВПРЫСКИВАЕМ AbilityQueuedState — дальше
        /// RunResolveAbilityQueueSystem (не загейчен) применит эффекты в этот же кадр.
        /// Пассив НЕ ре-резолвит цели (берёт из снапшота).
        /// </summary>
        private void ReplayAbility(ActionAbilityData s)
        {
            if (!_state.Value.TryGetEntity(s.SourceEntityKey, out int card))
            {
                Debug.LogError($"[Replay] Ability: card not found '{s.SourceEntityKey}' — desync");
                return;
            }
            if (!_abilityContainerPool.Value.Has(card))
            {
                Debug.LogWarning($"[Replay] Ability: card {card} has no AbilityContainerComponent");
                return;
            }

            var ids = _abilityContainerPool.Value.Get(card).AbilityEntities;
            if (ids == null || s.AbilityIndex < 0 || s.AbilityIndex >= ids.Length)
            {
                Debug.LogWarning($"[Replay] Ability: bad index {s.AbilityIndex} (count={ids?.Length ?? 0}) card={card}");
                return;
            }
            int abilityEntity = ids[s.AbilityIndex];

            var targets = new List<int>();
            if (s.TargetEntityKeys != null)
                foreach (var key in s.TargetEntityKeys)
                {
                    int t = ResolveTargetKey(key);
                    if (t >= 0) targets.Add(t);
                    else Debug.LogWarning($"[Replay] Ability: target key not resolved '{key}'");
                }

            // Составная способность: применяем эффекты СТАДИИ (StepIndex) из снапшота, а не через
            // обычную очередь. Контекст (KilledCount, выбранные случайные карты) — из снапшота.
            if (_abilityChainPool.Value.Has(abilityEntity))
            {
                ApplyChainStage(abilityEntity, card, s.StepIndex, targets, s.KilledCount, s.GeneratedExpansionIds, s.GeneratedCardIds);
                return;
            }

            if (!_queuedPool.Value.Has(abilityEntity)) _queuedPool.Value.Add(abilityEntity);
            ref var q = ref _queuedPool.Value.Get(abilityEntity);
            q.Targets = targets.ToArray();
            // Реплей впрыскивает способности ПО ОДНОЙ в порядке актива → кадры растут → порядок сохраняется.
            // Иерархию реакций пассиву воспроизводить не нужно: он не сортирует, а повторяет готовый порядок.
            q.Key = ActivationKey.Root(Time.frameCount, 0, 0);
            // Недетерм. генерация: кладём присланные активом идентичности — RunResolveAbilityQueueSystem
            // загрузит их в GeneratedCardChannel перед применением, эффект возьмёт вместо своего ролла.
            q.GeneratedExps = s.GeneratedExpansionIds;
            q.GeneratedIds  = s.GeneratedCardIds;

            // Модификаторы призыва: на пассиве InvokeSummon не выполняется (размещение — из ActionCastData
            // призванного), поэтому баффы/таймер навешиваем здесь, отложенно (см. ScheduleSummonModifiers).
            ScheduleSummonModifiers(abilityEntity, card, s.SummonedEntityKeys);

            Debug.Log($"[Replay] Ability inject: card={card} abilityIdx={s.AbilityIndex} ability={abilityEntity} targets={targets.Count}");
        }

        /// <summary>
        /// Пассив: применяет эффекты СТАДИИ цепочки из снапшота к присланным целям. Детерминированные
        /// эффекты (урон) ре-ранятся; недетерминированная генерация (GainRandomCardEffect) берёт
        /// присланные идентичности из GeneratedCardChannel. KilledCount — из снапшота (не пересчитываем).
        /// Эффекты стадии лежат в AbilityChainComponent (ChainStage.Effects = IEffect, видны Systems).
        ///
        /// VFX (2026-08-11): RunChainSystem (у которого эмиссия VFX стадии) крутится ТОЛЬКО у активного
        /// клиента — пассив раньше применял эффекты молча, без единого снаряда/луча/хита на экране
        /// оппонента. Эмитим ТУ ЖЕ косметику здесь, отдельно: Projectile — token=-1 (эффекты уже применены
        /// синхронно из снапшота выше, ждать прилёта нечего — снаряд летит чисто на вид).
        /// </summary>
        private void ApplyChainStage(int abilityEntity, int card, int stepIndex, List<int> targets,
                                     int killedCount, string[] genExps, int[] genIds)
        {
            var stages = _abilityChainPool.Value.Get(abilityEntity).Stages;
            if (stages == null || stepIndex < 0 || stepIndex >= stages.Length)
            {
                Debug.LogWarning($"[Replay] Chain: bad step {stepIndex} (count={stages?.Length ?? 0}) card={card}");
                return;
            }

            var stage = stages[stepIndex];

            ChainContext.CurrentKilled = killedCount;
            GeneratedCardChannel.LoadReplay(genExps, genIds);

            if (stage?.Effects != null)
                foreach (var t in targets)
                    foreach (var eff in stage.Effects)
                        if (eff != null && eff.IsReady)
                            eff.Apply(_world.Value, card, t);

            GeneratedCardChannel.ClearReplay();

            if (targets.Count > 0 && _abilityVfxPool.Value.Has(abilityEntity) && _boardView.Value != null)
            {
                var spec = _abilityVfxPool.Value.Get(abilityEntity).Spec;
                var targetsArr = targets.ToArray();
                if (spec != null && spec.Kind == VfxKind.Projectile && spec.Prefab != null)
                    VfxEmitUtil.LaunchProjectile(_world.Value, _boardView.Value, spec, card, targetsArr, -1);
                else
                    VfxEmitUtil.EmitInstantVfx(_world.Value, _boardView.Value, spec, card, targetsArr);
            }

            Debug.Log($"[Replay] Chain stage: card={card} step={stepIndex} targets={targets.Count} killed={killedCount}");
        }

        /// <summary>
        /// Планирует применение модификаторов призыва к призванным сущностям. Модификаторы берём из
        /// СВОЕЙ копии способности (ISummonModifierProvider) — данные идентичны активу (тот же ассет).
        /// Применяем отложенно: призванная сущность появляется отдельным ActionCastData позже.
        /// </summary>
        private void ScheduleSummonModifiers(int abilityEntity, int caster, string[] summonedKeys)
        {
            if (summonedKeys == null || summonedKeys.Length == 0) return;
            if (!_abilityEffectPool.Value.Has(abilityEntity)) return;

            var effects = _abilityEffectPool.Value.Get(abilityEntity).Effects;
            if (effects == null) return;

            List<IEffect> mods = null;
            foreach (var e in effects)
            {
                if (e is ISummonModifierProvider p && p.SummonModifiers != null && p.SummonModifiers.Count > 0)
                {
                    mods ??= new List<IEffect>();
                    mods.AddRange(p.SummonModifiers);
                }
            }
            if (mods == null) return;

            _pendingMods.Add(new PendingSummonMod
            {
                Caster = caster,
                Mods   = mods.ToArray(),
                Keys   = new List<string>(summonedKeys),
                Ttl    = 600,   // ~10с страховка от утечки, если ключ так и не разрешится
            });
            Debug.Log($"[Replay] scheduled {mods.Count} summon-mods for {summonedKeys.Length} entities (caster={caster})");
        }

        /// <summary>Каждый кадр пытается применить отложенные модификаторы к появившимся сущностям.</summary>
        private void ProcessPendingSummonMods()
        {
            if (_pendingMods.Count == 0) return;

            for (int i = _pendingMods.Count - 1; i >= 0; i--)
            {
                var p = _pendingMods[i];

                for (int k = p.Keys.Count - 1; k >= 0; k--)
                {
                    if (!_state.Value.TryGetEntity(p.Keys[k], out int target)) continue;
                    foreach (var mod in p.Mods)
                        if (mod != null && mod.IsReady)
                            mod.Apply(_world.Value, p.Caster, target);
                    p.Keys.RemoveAt(k);
                }

                p.Ttl--;
                if (p.Keys.Count == 0 || p.Ttl <= 0)
                    _pendingMods.RemoveAt(i);
            }
        }

        /// <summary>Временный контроль истёк (актив посчитал ходы) → откатываем владельца по локальной
        /// TempControlledComponent (исходный OwnerId/теги). Не тикаем сами — применяем присланный откат.</summary>
        private void ReplayControlRevert(ActionControlRevertData s)
        {
            if (!_state.Value.TryGetEntity(s.EntityKey, out int entity))
            {
                Debug.LogError($"[Replay] ControlRevert: entity not found '{s.EntityKey}' — desync");
                return;
            }
            TempControlRevertSystem.RevertControl(_world.Value, entity);
            Debug.Log($"[Replay] ControlRevert: entity={entity}");
        }

        /// <summary>Смерть от системного таймера (актив посчитал) → вешаем DeadTag, DieSystem отработает.</summary>
        private void ReplayDeath(ActionDeathData s)
        {
            if (!_state.Value.TryGetEntity(s.EntityKey, out int entity))
            {
                Debug.LogError($"[Replay] Death: entity not found '{s.EntityKey}' — desync");
                return;
            }
            if (!_deadPool.Value.Has(entity)) _deadPool.Value.Add(entity);
            Debug.Log($"[Replay] Death (timer): entity={entity}");
        }

        /// <summary>
        /// Замена добора оппонента (Адовый червь): повторяем результат у игрока s.PlayerId. Предложенные
        /// убираем из колоды; выбранную → кладбище (DestroyChosen), остальные → в руку. Карты по ключам
        /// (есть у обоих с мулигана). Рука оппонента скрыта → без CardDrawnEvent (только game-state).
        /// </summary>
        private void ReplayDrawReplacement(ActionDrawReplacementData s)
        {
            int player = -1;
            foreach (var pe in _playerFilter.Value)
                if (_playerPool.Value.Get(pe).PlayerId == s.PlayerId) { player = pe; break; }
            if (player < 0) { Debug.LogError($"[Replay] DrawReplacement: player {s.PlayerId} not found"); return; }

            int chosen = -1;
            if (!string.IsNullOrEmpty(s.ChosenKey)) _state.Value.TryGetEntity(s.ChosenKey, out chosen);

            ref var deck = ref _deckPool.Value.Get(player);
            ref var hand = ref _handPool.Value.Get(player);

            if (s.OfferedKeys != null)
                foreach (var key in s.OfferedKeys)
                {
                    if (!_state.Value.TryGetEntity(key, out int card)) continue;

                    if (deck.CardEntities != null) deck.CardEntities.Remove(card);
                    if (_deckTagPool.Value.Has(card)) _deckTagPool.Value.Del(card);

                    bool destroy = s.DestroyChosen ? (card == chosen) : (card != chosen);
                    if (destroy)
                    {
                        if (!_graveTagPool.Value.Has(card)) _graveTagPool.Value.Add(card);
                    }
                    else
                    {
                        if (!_handTagPool.Value.Has(card)) _handTagPool.Value.Add(card);
                        if (hand.CardEntities != null) hand.CardEntities.Add(card);
                    }
                }

            deck.Count = deck.CardEntities != null ? deck.CardEntities.Count : 0;
            hand.Count = hand.CardEntities != null ? hand.CardEntities.Count : 0;

            Debug.Log($"[Replay] DrawReplacement: player={player} offered={s.OfferedKeys?.Length ?? 0} chosen={chosen}");
        }

        /// <summary>Добор оппонента (turn-start): у игрока s.PlayerId снимаем s.Count верхних карт колоды в
        /// руку через DrawCardEvent{Sync=false}. DrawCardSystem сделает перенос + CardDrawnEvent (счётчик
        /// DrawnByModelId, триггеры добора — гейтятся AbilityFire; рука оппонента не локальная → в мою UI не
        /// попадёт). Колоды order-синхронны (мулиган + детерм. генерация) → снимаются те же карты. Sync=false →
        /// эха обратно не будет.</summary>
        private void ReplayDraw(ActionDrawData s)
        {
            int player = -1;
            foreach (var pe in _playerFilter.Value)
                if (_playerPool.Value.Get(pe).PlayerId == s.PlayerId) { player = pe; break; }
            if (player < 0) { Debug.LogError($"[Replay] Draw: player {s.PlayerId} not found"); return; }

            // ТОЧНЫЙ синк: переносим в руку ИМЕННО те карты, что добрал актив (по ключу), а не «верхние N» —
            // иначе при любом расхождении порядка колоды зеркала тянут разные карты → фантомы в руке оппонента.
            if (s.DrawnKeys != null && s.DrawnKeys.Length > 0)
            {
                int moved = 0;
                foreach (var key in s.DrawnKeys)
                {
                    if (!_state.Value.TryGetEntity(key, out int card))
                    {
                        Debug.LogWarning($"[Replay] Draw: card key not resolved '{key}'");
                        continue;
                    }
                    MoveDeckCardToHand(player, card);
                    moved++;
                }
                Debug.Log($"[Replay] Draw(keys): player={player} moved={moved}");
                return;
            }

            // Фолбэк (старые снапшоты без ключей): добор N верхних.
            if (!_drawCardPool.Value.Has(player)) _drawCardPool.Value.Add(player);
            ref var d = ref _drawCardPool.Value.Get(player);
            d.Count = s.Count;
            d.Sync = false;   // реплей — синк обратно не нужен
            Debug.Log($"[Replay] Draw: player={player} count={s.Count}");
        }

        // Перенос конкретной карты колода→рука у зеркала оппонента (тег + список + CardDrawnEvent для UI/триггеров).
        private void MoveDeckCardToHand(int playerEntity, int card)
        {
            if (_deckTagPool.Value.Has(card)) _deckTagPool.Value.Del(card);
            if (_deckPool.Value.Has(playerEntity))
            {
                ref var deck = ref _deckPool.Value.Get(playerEntity);
                deck.CardEntities?.Remove(card);
                deck.Count = deck.CardEntities?.Count ?? 0;
            }

            if (!_handTagPool.Value.Has(card)) _handTagPool.Value.Add(card);
            if (_handPool.Value.Has(playerEntity))
            {
                ref var hand = ref _handPool.Value.Get(playerEntity);
                hand.CardEntities ??= new List<int>();
                if (!hand.CardEntities.Contains(card)) hand.CardEntities.Add(card);
                hand.Count = hand.CardEntities.Count;
            }

            GameEventBus.Publish(new CardDrawnEvent { CardEntity = card, PlayerId = playerEntity });
        }

        /// <summary>EndTurn оппонента → выдаём ход ЛОКАЛЬНОМу игроку (он становится активным).</summary>
        private void ReplayEndTurn(ActionEndTurnData s)
        {
            int local = -1;
            foreach (var pe in _localPlayerFilter.Value) { local = pe; break; }
            if (local < 0) { Debug.LogError("[Replay] EndTurn: local player not found"); return; }

            // Откат временных множителей ОППОНЕНТА (его ход кончился) — зеркально активу (EndTurn Step B).
            int localId = _playerPool.Value.Get(local).PlayerId;
            foreach (var pe in _playerFilter.Value)
            {
                int pid = _playerPool.Value.Get(pe).PlayerId;
                if (pid != localId) CastMultiplierService.TickOwnerTurnEnd(pid);
            }

            TurnFlow.GrantTurn(_world.Value, local, s.TurnNumber + 1);
            Debug.Log($"[Replay] EndTurn → grant turn to local player {local} (turn {s.TurnNumber + 1})");
        }

        /// <summary>Ключ цели → локальная сущность: "PLAYER:{id}" → entity игрока; иначе по NetworkEntityKey.</summary>
        private int ResolveTargetKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return -1;

            if (key.StartsWith("PLAYER:"))
            {
                if (int.TryParse(key.Substring("PLAYER:".Length), out int pid))
                    foreach (var pe in _playerFilter.Value)
                        if (_playerPool.Value.Get(pe).PlayerId == pid)
                            return pe;
                return -1;
            }

            return _state.Value.TryGetEntity(key, out int e) ? e : -1;
        }

        private int OwnerId(int card)
            => _ownerPool.Value.Has(card) ? _ownerPool.Value.Get(card).OwnerId : -1;

        /// <summary>Действие исходит от НАШЕЙ карты? (тогда это эхо собственного снапшота — реплеить нельзя).</summary>
    }
}
