using Fusion;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Mono;
using Game.Core.Photon;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using System.Linq;
using UnityEngine;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Создаёт entity локального игрока.
    /// PlayerId = Photon ActorNumber.
    /// Side = 1 если ActorNumber наименьший среди подключённых, иначе 2.
    /// Вешает GoldComponent, ManaComponent, DeckComponent, HandComponent.
    /// </summary>
    public sealed class InitPlayerSystem : IEcsInitSystem
    {
        readonly EcsWorldInject _world = default;
        readonly EcsSharedInject<IGameStateContext> _state = default;
        readonly EcsCustomInject<PhotonRunHandler> _handler = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsPoolInject<PlayerSideComponent> _sidePool = default;
        readonly EcsPoolInject<GoldComponent> _goldPool = default;
        readonly EcsPoolInject<ManaComponent> _manaPool = default;
        readonly EcsPoolInject<DeckComponent> _deckPool = default;
        readonly EcsPoolInject<HandComponent> _handPool = default;
        readonly EcsPoolInject<LocalComponent> _localPool = default;
        readonly EcsPoolInject<RemoteComponent> _remotePool = default;
        readonly EcsPoolInject<AiPlayerComponent> _aiPool = default;
        readonly EcsPoolInject<HealthComponent> _healthPool = default;
        readonly EcsPoolInject<AttackComponent> _attackPool = default;
        readonly EcsPoolInject<AvatarViewComponent> _avatarViewPool = default;
        readonly EcsCustomInject<BoardView> _boardView = default;

        public void Init(IEcsSystems systems)
        {
            // ── PvE: сети нет — создаём обоих игроков локально (человек side 1, ИИ side 2). ──
            if (Game.Core.Service.PveMode.Enabled)
            {
                InitPve();
                return;
            }

            // ── РЕКОННЕКТ: идентичность из сохранённой записи, а НЕ из порядка джойна. ──
            if (Network.ReconnectFlow.IsActive)
            {
                InitReconnect();
                return;
            }

            var runner = PhotonInitializer.Instance?.Runner;
            if (runner == null)
            {
                Debug.LogError("[InitLocalPlayerSystem] NetworkRunner not found");
                return;
            }

            PlayerRef[] players = PhotonInitializer.Instance.ConnectedPlayers.ToArray();

            int localPlayerID = runner.LocalPlayer.PlayerId;

            // Детерминированно: меньший actorNumber = Side 1, больший = Side 2
            int minActorNumber = players.Min(p => p.PlayerId);

            bool isLocalPlayer = false;

            // Какие стороны реально заняты игроками — остальные аватары (напр. оппонент в соло) прячем.
            var sidePresent = new bool[3];   // индекс = сторона 1/2

            for (int i = 0; i < players.Length; i++)
            {
                int actorNumber = players[i].PlayerId; 

                isLocalPlayer = actorNumber == localPlayerID;

                int side = actorNumber == minActorNumber ? 1 : 2;
                if (side >= 1 && side <= 2) sidePresent[side] = true;

                int entity = _world.Value.NewEntity();

                ref var player = ref _playerPool.Value.Add(entity);
                player.PlayerId = actorNumber;
                player.IsLocalPlayer = isLocalPlayer;

                ref var playerSide = ref _sidePool.Value.Add(entity);
                playerSide.Side = side;

                ref var gold = ref _goldPool.Value.Add(entity);
                gold.Current = 0;
                gold.Max = 0;

                ref var mana = ref _manaPool.Value.Add(entity);
                mana.Current = 0;
                mana.Max = 0;

                ref var deck = ref _deckPool.Value.Add(entity);
                deck.CardEntities = new();
                deck.Count = 0;

                ref var hand = ref _handPool.Value.Add(entity);
                hand.CardEntities = new();
                hand.Count = 0;

                ref var healthComp = ref _healthPool.Value.Add(entity);
                healthComp.Current = 30;
                healthComp.Max = 30;
                healthComp.BaseMax = 30;   // без этого RecalculateValue (после DrainHealth и любого HP-модификатора) обнулит базу → Max = Σмодов

                ref var attackComp = ref _attackPool.Value.Add(entity);
                attackComp.Base = 1;   // базовая атака аватара — «отбиваться» от захватчиков row0 своей стороны
                attackComp.RecalculateValue();

                if (_boardView.Value != null)
                {
                    var avatarView = _boardView.Value.GetAvatarView(side);
                    if (avatarView != null)
                    {
                        ref var avatarViewComp = ref _avatarViewPool.Value.Add(entity);
                        avatarViewComp.View = avatarView.gameObject;

                        // Косметика: локальному игроку ставим НАДЕТЫЙ аватар. Оппонента (сеть) подменит синк
                        // по приезду его avatarId (RPC), ИИ в PvE — дефолт/энкаунтер.
                        if (isLocalPlayer) ApplyEquippedAvatar(avatarView);
                    }
                }

                if (isLocalPlayer)
                {
                    _state.Value.AddEntity(entity, Service.EntityService.PLAYER_ENTITY);
                    _localPool.Value.Add(entity);
                    // Префикс коротких NetworkEntityKey = id локального клиента (разводит ключи двух клиентов
                    // → ноль коллизий). Ставится до генерации колоды (InitDeckSystem идёт после).
                    Network.NetKey.SetClientPrefix(actorNumber);
                }
                else
                {
                    _state.Value.AddEntity(entity, Service.EntityService.OPPONENT_ENTITY);
                    _remotePool.Value.Add(entity);
                }

                GameEventBus.Publish(new PlayerAssignedEvent
                {
                    PlayerEntity = entity,
                    Side = side,
                    IsLocalPlayer = isLocalPlayer,
                    PlayerId = actorNumber
                });

                Debug.Log($"[InitLocalPlayerSystem] Player entity={entity} playerId={actorNumber} side={side} isLocal={isLocalPlayer}");
            }

            // Прячем аватар + аватар-клетку для незанятых сторон (напр. оппонент в соло-режиме без 2-го игрока).
            if (_boardView.Value != null)
            {
                for (int side = 1; side <= 2; side++)
                {
                    if (sidePresent[side]) continue;
                    _boardView.Value.GetAvatarView(side)?.gameObject.SetActive(false);
                    _boardView.Value.GetAvatarCell(side)?.gameObject.SetActive(false);
                }
            }
        }

        // Косметика: подменить визуал аватара на НАДЕТЫЙ (EquippedAvatar → AvatarConfig → Prefab). Резолв тут
        // (Ecs видит Configs/Instance), а AvatarPlayerView получает готовый GameObject — Mono не тянет Configs.
        static void ApplyEquippedAvatar(Mono.AvatarPlayerView view)
        {
            if (view == null) return;
            string itemId = Service.EquippedAvatar.ItemId;
            if (string.IsNullOrEmpty(itemId)) return;   // не выбран → дефолт из префаба
            var cfg = Configs.AvatarConfig.Instance;
            var avatar = cfg != null ? cfg.Get(itemId) : null;
            if (avatar != null && avatar.Prefab != null) view.SetAvatarVisual(avatar.Prefab);
        }

        // ── РЕКОННЕКТ: игроки восстанавливаются из MatchSessionStore, а НЕ из порядка джойна Fusion. ──
        // При повторном входе Fusion выдаёт ДРУГОЙ слот, а от PlayerId зависят сторона доски, камера и
        // ПРЕФИКС сетевых ключей (NetKey.SetClientPrefix): с новым слотом ключи разъехались бы и реплей
        // перестал бы находить карты. Колода/рука/мулиган здесь НЕ создаются (EcsRunHandler пропускает эти
        // системы) — всё состояние приедет снэпшотом мира от пира (WorldResyncSystem).
        void InitReconnect()
        {
            var rec = Network.ReconnectFlow.Saved;
            int mySide  = rec.MySide == 2 ? 2 : 1;
            int oppSide = mySide == 1 ? 2 : 1;

            CreateNetPlayer(rec.MyPlayerId, mySide, isLocal: true);
            CreateNetPlayer(rec.OpponentPlayerId, oppSide, isLocal: false);

            // Свои ресурсы и личный счётчик ходов — из ЛОКАЛЬНОЙ записи: пассив не зеркалит чужой доход,
            // поэтому в снэпшоте пира наши золото/мана протухшие (его Apply применяет ресурсы только для
            // игрока-отправителя). Дальше они и так пересчитаются на старте нашего хода (Max += доход).
            var counterPool = _world.Value.GetPool<TurnCounterComponent>();
            foreach (var pe in _world.Value.Filter<PlayerComponent>().Inc<LocalComponent>().End())
            {
                if (_goldPool.Value.Has(pe)) { ref var g = ref _goldPool.Value.Get(pe); g.Current = rec.Gold; g.Max = rec.GoldMax; }
                if (_manaPool.Value.Has(pe)) { ref var m = ref _manaPool.Value.Get(pe); m.Current = rec.Mana; m.Max = rec.ManaMax; }
                if (!counterPool.Has(pe)) counterPool.Add(pe);
                counterPool.Get(pe).Personal = rec.PersonalTurn;
            }

            Debug.Log($"[InitLocalPlayerSystem] РЕКОННЕКТ: я id={rec.MyPlayerId} side={mySide}, оппонент id={rec.OpponentPlayerId} side={oppSide}");
        }

        // Сетевой игрок с ЗАДАННОЙ идентичностью (реконнект). Тело повторяет обычную ветку Init, но id/side
        // приходят снаружи, а не из ConnectedPlayers — намеренно отдельным методом, чтобы не трогать рабочий
        // путь первого входа (та же развязка, что InitPve/CreatePvePlayer).
        void CreateNetPlayer(int playerId, int side, bool isLocal)
        {
            int entity = _world.Value.NewEntity();

            ref var player = ref _playerPool.Value.Add(entity);
            player.PlayerId = playerId;
            player.IsLocalPlayer = isLocal;

            _sidePool.Value.Add(entity).Side = side;

            ref var gold = ref _goldPool.Value.Add(entity);
            gold.Current = 0; gold.Max = 0;
            ref var mana = ref _manaPool.Value.Add(entity);
            mana.Current = 0; mana.Max = 0;

            ref var deck = ref _deckPool.Value.Add(entity);
            deck.CardEntities = new(); deck.Count = 0;
            ref var hand = ref _handPool.Value.Add(entity);
            hand.CardEntities = new(); hand.Count = 0;

            ref var health = ref _healthPool.Value.Add(entity);
            health.Current = 30; health.Max = 30; health.BaseMax = 30;   // точные HP приедут снэпшотом

            ref var attack = ref _attackPool.Value.Add(entity);
            attack.Base = 1;
            attack.RecalculateValue();

            if (_boardView.Value != null)
            {
                var avatarView = _boardView.Value.GetAvatarView(side);
                if (avatarView != null)
                {
                    _avatarViewPool.Value.Add(entity).View = avatarView.gameObject;
                    if (isLocal) ApplyEquippedAvatar(avatarView);
                }
            }

            if (isLocal)
            {
                _state.Value.AddEntity(entity, Service.EntityService.PLAYER_ENTITY);
                _localPool.Value.Add(entity);
                Network.NetKey.SetClientPrefix(playerId);   // тот же префикс, что был до обрыва
            }
            else
            {
                _state.Value.AddEntity(entity, Service.EntityService.OPPONENT_ENTITY);
                _remotePool.Value.Add(entity);
            }

            GameEventBus.Publish(new PlayerAssignedEvent
            {
                PlayerEntity = entity,
                Side = side,
                IsLocalPlayer = isLocal,
                PlayerId = playerId
            });

            Debug.Log($"[InitLocalPlayerSystem] Player entity={entity} playerId={playerId} side={side} isLocal={isLocal} (реконнект)");
        }

        // ── PvE: человек (PlayerId=1, side 1, Local) + ИИ (PlayerId=2, side 2, AiPlayerComponent). ──
        // ИИ БЕЗ RemoteComponent: он не сетевой — соло/PvE-ветки (EndTurnRequestSystem) различают его
        // по AiPlayerComponent. Камера — сторона 1 (человек).
        void InitPve()
        {
            // Камера: BattleCameraSelector сам ловит PlayerAssignedEvent локального игрока (side 1).
            CreatePvePlayer(playerId: 1, side: 1, isLocal: true);
            CreatePvePlayer(playerId: 2, side: 2, isLocal: false);
            Debug.Log("[InitLocalPlayerSystem] PvE: созданы человек (side 1) и ИИ (side 2)");
        }

        void CreatePvePlayer(int playerId, int side, bool isLocal)
        {
            int entity = _world.Value.NewEntity();

            ref var player = ref _playerPool.Value.Add(entity);
            player.PlayerId = playerId;
            player.IsLocalPlayer = isLocal;

            _sidePool.Value.Add(entity).Side = side;

            ref var gold = ref _goldPool.Value.Add(entity);
            gold.Current = 0; gold.Max = 0;
            ref var mana = ref _manaPool.Value.Add(entity);
            mana.Current = 0; mana.Max = 0;

            ref var deck = ref _deckPool.Value.Add(entity);
            deck.CardEntities = new(); deck.Count = 0;
            ref var hand = ref _handPool.Value.Add(entity);
            hand.CardEntities = new(); hand.Count = 0;

            ref var health = ref _healthPool.Value.Add(entity);
            health.Current = 30; health.Max = 30; health.BaseMax = 30;   // BaseMax обязателен для RecalculateValue; HP ИИ переопределит InitPveOpponentSystem

            ref var attack = ref _attackPool.Value.Add(entity);
            attack.Base = 1;
            attack.RecalculateValue();

            if (_boardView.Value != null)
            {
                var avatarView = _boardView.Value.GetAvatarView(side);
                if (avatarView != null)
                {
                    _avatarViewPool.Value.Add(entity).View = avatarView.gameObject;
                    if (isLocal) ApplyEquippedAvatar(avatarView);   // человеку — надетый аватар (ИИ — дефолт)
                }
            }

            if (isLocal)
            {
                _state.Value.AddEntity(entity, Service.EntityService.PLAYER_ENTITY);
                _localPool.Value.Add(entity);
                Network.NetKey.SetClientPrefix(playerId);   // ключи человека "1-N" (ИИ получает "2-N" в InitPveOpponentSystem)
            }
            else
            {
                _state.Value.AddEntity(entity, Service.EntityService.OPPONENT_ENTITY);
                _aiPool.Value.Add(entity);                  // ИИ, НЕ Remote
            }

            GameEventBus.Publish(new PlayerAssignedEvent
            {
                PlayerEntity = entity,
                Side = side,
                IsLocalPlayer = isLocal,
                PlayerId = playerId
            });

            Debug.Log($"[InitLocalPlayerSystem] Player entity={entity} playerId={playerId} side={side} isLocal={isLocal} (PvE)");
        }
    }
}
