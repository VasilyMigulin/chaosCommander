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
    public sealed class PveScriptedEventSystem : IEcsInitSystem, IEcsRunSystem, System.IDisposable
    {
        readonly EcsWorldInject _world = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsPoolInject<HealthComponent> _healthPool = default;
        readonly EcsPoolInject<TakeDamageEvent> _damagePool = default;
        readonly EcsFilterInject<Inc<PlayerComponent, LocalComponent>> _humanFilter = default;
        readonly EcsFilterInject<Inc<PlayerComponent, AiPlayerComponent>> _aiFilter = default;

        readonly HashSet<int> _fired = new();   // индексы уже сработавших событий
        int _currentGlobalTurn;
        bool _subscribed;
        int _spawnCounter;                      // уникальные ключи "scr-N" для создаваемых карт

        public void Init(IEcsSystems systems)
        {
            GameEventBus.Subscribe<TurnStartedEvent>(OnTurnStarted);
            _subscribed = true;
        }

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
                        SpawnCards(action, target, toHand: action.Kind == PveEncounterConfig.ScriptedActionKind.AddCardToHand);
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

        void SpawnCards(in PveEncounterConfig.ScriptedAction action, int targetPlayerEntity, bool toHand)
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
                });
            }
            Debug.Log($"[PveScript] {(toHand ? "выдал в руку" : "втасовал в колоду")} {count}× '{action.Card.name}' игроку {player.PlayerId}");
        }
    }
}
