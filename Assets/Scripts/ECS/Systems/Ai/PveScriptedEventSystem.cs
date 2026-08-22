using System.Collections.Generic;
using Game.Core.Configs;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Service;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using UnityEngine;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// PvE: исполняет СКРИПТОВАННЫЕ события боя из PveEncounterConfig.ScriptedEvents — сюжетные
    /// вмешательства НЕ-участников («Гнидальф появляется, говорит и мешает игроку 2 Вонючих облака»).
    /// Триггеры: старт хода N (глобальный) / HP игрока|ИИ ниже порога. Каждое событие — ОДИН раз за бой.
    /// Действия: реплики (StoryLineUIEvent → StoryDialogueView) + втасовка/выдача карт (CreateCardEvent —
    /// тот же путь, что у генерации: детерминированная позиция втасовки) + урон аватару (TakeDamageEvent —
    /// штатный канал урона, GameOverCheck сработает как обычно). В MP-режиме система пассивна.
    /// Регистрация: в _turnSystems после RunTurnStartSystem (действия — bus/компоненты, потребители позже в кадре).
    /// </summary>
    public sealed class PveScriptedEventSystem : IEcsInitSystem, IEcsRunSystem, IEcsDestroySystem, System.IDisposable
    {
        readonly EcsWorldInject _world = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsPoolInject<HealthComponent> _healthPool = default;
        readonly EcsPoolInject<TakeDamageEvent> _damagePool = default;
        readonly EcsPoolInject<AutoCastComponent> _autoCastPool = default;
        readonly EcsPoolInject<ForceRandomTargetingComponent> _forceRandomPool = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;
        readonly EcsFilterInject<Inc<PlayerComponent, LocalComponent>> _humanFilter = default;
        readonly EcsFilterInject<Inc<PlayerComponent, AiPlayerComponent>> _aiFilter = default;
        readonly EcsFilterInject<Inc<CommanderTag, HandTag, OwnerComponent>> _commanderInHandFilter = default;

        readonly HashSet<int> _fired = new();   // индексы уже сработавших событий
        int _currentGlobalTurn;
        bool _subscribed;
        int _spawnCounter;                      // уникальные ключи "scr-N" для создаваемых карт

        public void Init(IEcsSystems systems)
        {
            GameEventBus.Subscribe<TurnStartedEvent>(OnTurnStarted);
            _subscribed = true;
        }

        // EcsSystems.Destroy() ищет IEcsDestroySystem, не System.IDisposable — без этого моста Dispose()
        // фреймворк никогда не вызывал бы (см. EcsRunHandler/TutorialEcsHandler.Dispose → _allSystems.Destroy()).
        public void Destroy(IEcsSystems systems) => Dispose();

        public void Dispose()
        {
            if (!_subscribed) return;
            GameEventBus.Unsubscribe<TurnStartedEvent>(OnTurnStarted);
            _subscribed = false;
        }

        void OnTurnStarted(TurnStartedEvent e) => _currentGlobalTurn = e.TurnNumber;

        public void Run(IEcsSystems systems)
        {
            if (!PveMode.Enabled || MatchState.IsOver) return;

            var enc = PveEncounterLocator.Current;
            if (enc == null || enc.ScriptedEvents == null || enc.ScriptedEvents.Length == 0) return;
            if (_fired.Count >= enc.ScriptedEvents.Length) return;   // всё отстреляло

            int human = -1, ai = -1;
            foreach (var e in _humanFilter.Value) { human = e; break; }
            foreach (var e in _aiFilter.Value) { ai = e; break; }

            for (int i = 0; i < enc.ScriptedEvents.Length; i++)
            {
                if (_fired.Contains(i)) continue;
                ref readonly var evt = ref enc.ScriptedEvents[i];

                bool trigger = evt.Trigger switch
                {
                    PveEncounterConfig.ScriptedTriggerKind.OnGlobalTurn
                        => _currentGlobalTurn >= evt.TurnNumber && evt.TurnNumber > 0,
                    PveEncounterConfig.ScriptedTriggerKind.OnPlayerHpBelow
                        => human >= 0 && _healthPool.Value.Has(human) && _healthPool.Value.Get(human).Current < evt.HpThreshold,
                    PveEncounterConfig.ScriptedTriggerKind.OnAiHpBelow
                        => ai >= 0 && _healthPool.Value.Has(ai) && _healthPool.Value.Get(ai).Current < evt.HpThreshold,
                    _ => false,
                };
                if (!trigger) continue;

                _fired.Add(i);
                Debug.Log($"[PveScript] событие #{i} '{evt.Note}' сработало (trigger={evt.Trigger})");
                Execute(evt, human, ai);
            }
        }

        void Execute(in PveEncounterConfig.ScriptedEvent evt, int human, int ai)
        {
            // 1) Говорящая голова.
            if (evt.Lines != null)
            {
                foreach (var line in evt.Lines)
                {
                    if (line.Portrait == null && string.IsNullOrEmpty(line.TextKey) && string.IsNullOrEmpty(line.FallbackText))
                        continue;
                    GameEventBus.Publish(new StoryLineUIEvent
                    {
                        Portrait     = line.Portrait,
                        SpeakerKey   = line.SpeakerKey,
                        TextKey      = line.TextKey,
                        FallbackText = line.FallbackText,
                        Duration     = line.Duration <= 0f ? 3f : line.Duration,
                    });
                }
            }

            // 2) Игровые действия.
            if (evt.Actions == null) return;
            foreach (var action in evt.Actions)
            {
                int target = action.TargetPlayer ? human : ai;
                if (target < 0) continue;

                switch (action.Kind)
                {
                    case PveEncounterConfig.ScriptedActionKind.ShuffleCardToDeck:
                    case PveEncounterConfig.ScriptedActionKind.AddCardToHand:
                        SpawnCards(action, target, ai, toHand: action.Kind == PveEncounterConfig.ScriptedActionKind.AddCardToHand);
                        break;

                    case PveEncounterConfig.ScriptedActionKind.PlayCard:
                        PlayCard(action, target);
                        break;

                    case PveEncounterConfig.ScriptedActionKind.PlayCommander:
                        PlayCommander(target);
                        break;

                    case PveEncounterConfig.ScriptedActionKind.DealDamage:
                        // Штатный канал урона: TakeDamageSystem → HP/эффекты/GameOverCheck. Attacker=-1 (сюжет).
                        if (_healthPool.Value.Has(target))
                        {
                            if (!_damagePool.Value.Has(target)) _damagePool.Value.Add(target);
                            ref var d = ref _damagePool.Value.Get(target);
                            d.Amount += Mathf.Max(1, action.Amount);
                            d.Attacker = -1;
                        }
                        break;
                }
            }
        }

        void SpawnCards(in PveEncounterConfig.ScriptedAction action, int targetPlayerEntity, int aiPlayerEntity, bool toHand)
        {
            if (action.Card == null || action.Card.CardData == null)
            {
                Debug.LogWarning("[PveScript] действие Shuffle/AddCard без карты — пропущено");
                return;
            }

            ref var player = ref _playerPool.Value.Get(targetPlayerEntity);
            int count = Mathf.Max(1, action.Amount);
            for (int i = 0; i < count; i++)
            {
                GameEventBus.Publish(new CreateCardEvent
                {
                    ExpansionId        = action.Card.ExpansionId,
                    CardId             = action.Card.CardId,
                    NetworkEntityKey   = "scr-" + _spawnCounter++,   // PvE: синка нет, нужен лишь уникальный ключ
                    PlayerOwnerEntity  = targetPlayerEntity,
                    OwnerId            = player.PlayerId,
                    IsEnemy            = !player.IsLocalPlayer,
                    InHand             = toHand,                      // false → колода (детерм. втасовка)
                    RegisterInZoneList = true,
                    // UI-анимация «замешалось в колоду» (CardShuffledToDeckEvent) — у сюжетного триггера нет
                    // карты-кастера на столе (портрет — не сущность), источник ВСЕГДА аватар ИИ («злодей
                    // проклял вашу колоду»). Для AddCardToHand (toHand=true) поле не участвует — у добора
                    // в руку своя, отдельная логика источника (CardDrawnEvent), эту не трогаем.
                    SourceEntity       = toHand ? (int?)null : aiPlayerEntity,
                });
            }
            Debug.Log($"[PveScript] {(toHand ? "выдал в руку" : "втасовал в колоду")} {count}× '{action.Card.name}' игроку {player.PlayerId}");
        }

        /// <summary>PlayCard: НАСТОЯЩИЙ розыгрыш карты (не тихий спавн) — «Главарь зовёт Зверя!» и Зверь
        /// приходит СО своим «при разыгрывании». Путь — тот же, что у Фокус-покуса/Йогг-Сарона (см.
        /// AbilityGenerate.Spawn): создаём карту в руке владельца с AutoCastComponent, дальше её ведёт
        /// штатный пайплайн — AutoCastSystem кладёт в очередь (по одной, с пейсингом) → RequestCardCastEvent →
        /// RunCastRouterSystem: бесплатная оплата (Free), делегирование по типу (существо/спелл/чары),
        /// для существа БЕЗ живого игрока — автовыбор свободной клетки фронт-ряда И InvokeEvent (своё
        /// OnCast сработает на размещении), для спелла/чар — CardCastEvent сразу (эффект резолвится по-настоящему).
        /// RegisterInZoneList=false — карта не всплывает в UI руки перед тем, как тут же уйти в каст.
        /// ForceRandomTarget=true — спросить некого (как у прочих авто-кастов), карта сама решит цель.</summary>
        void PlayCard(in PveEncounterConfig.ScriptedAction action, int targetPlayerEntity)
        {
            if (action.Card == null || action.Card.CardData == null)
            {
                Debug.LogWarning("[PveScript] действие PlayCard без карты — пропущено");
                return;
            }

            ref var player = ref _playerPool.Value.Get(targetPlayerEntity);
            int ownerId = player.PlayerId;
            int count = Mathf.Max(1, action.Amount);
            for (int i = 0; i < count; i++)
            {
                GameEventBus.Publish(new CreateCardEvent
                {
                    ExpansionId        = action.Card.ExpansionId,
                    CardId             = action.Card.CardId,
                    NetworkEntityKey   = "scr-" + _spawnCounter++,
                    PlayerOwnerEntity  = targetPlayerEntity,
                    OwnerId            = ownerId,
                    IsEnemy            = !player.IsLocalPlayer,
                    InHand             = true,
                    RegisterInZoneList = false,
                    AutoCast           = true,
                    ForceRandomTarget  = true,
                });
            }
            Debug.Log($"[PveScript] разыграл {count}× '{action.Card.name}' игроку {ownerId} (полноценный каст, не тихий спавн)");
        }

        /// <summary>PlayCommander: форс-розыгрыш КОМАНДИРА цели из его РУКИ (тот же AutoCast-путь, что у
        /// PlayCard — RunCastRouterSystem бесплатно оплатит и разыграет по-настоящему). В отличие от PlayCard
        /// не создаёт карту — командир уже существует сущностью (CardCreatureModel ставит CommanderTag при
        /// инициализации, DieSystem возвращает его В РУКУ ТОЙ ЖЕ сущностью), просто вешаем AutoCastComponent
        /// на неё. Командира сейчас нет в руке (уже на столе / на кулдауне после смерти) → предупреждение,
        /// RunCastRouterSystem и так отклонил бы кулдаун — но там уже списалась бы попытка впустую.</summary>
        void PlayCommander(int targetPlayerEntity)
        {
            ref var player = ref _playerPool.Value.Get(targetPlayerEntity);
            int commander = FindCommanderInHand(player.PlayerId);
            if (commander < 0)
            {
                Debug.LogWarning($"[PveScript] PlayCommander: у игрока {player.PlayerId} нет командира в руке (уже на столе или на кулдауне) — пропущено");
                return;
            }

            if (!_autoCastPool.Value.Has(commander)) _autoCastPool.Value.Add(commander);
            _autoCastPool.Value.Get(commander).Free = true;
            // Спросить некого (форс от сюжета, не рукой игрока) — как у прочих авто-кастов: если у
            // командира есть «при разыгрывании» с выбором цели, оно само решит (Random).
            if (!_forceRandomPool.Value.Has(commander)) _forceRandomPool.Value.Add(commander);

            Debug.Log($"[PveScript] форс-розыгрыш командира игрока {player.PlayerId}");
        }

        int FindCommanderInHand(int ownerId)
        {
            foreach (var e in _commanderInHandFilter.Value)
                if (_ownerPool.Value.Get(e).OwnerId == ownerId) return e;
            return -1;
        }
    }
}
