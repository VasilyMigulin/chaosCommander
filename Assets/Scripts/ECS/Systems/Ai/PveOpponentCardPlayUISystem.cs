using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Service;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// PvE: показ «что разыграл оппонент» (выкат карты слева). В MP это публикует ReplayActionSystem
    /// при реплее ActionCastData; в PvE реплея нет — ловим CardCastEvent карт ИИ-игрока напрямую.
    /// Призывы попап не дают: они идут с NotCast и CardCastEvent не публикуют (как в MP).
    /// В MP-режиме система пассивна (гейт PveMode.Enabled).
    /// </summary>
    public sealed class PveOpponentCardPlayUISystem : IEcsInitSystem, System.IDisposable
    {
        readonly EcsWorldInject _world = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsPoolInject<AiPlayerComponent> _aiPool = default;
        readonly EcsPoolInject<CardModelComponent> _modelPool = default;
        readonly EcsPoolInject<CardViewDataComponent> _viewPool = default;
        readonly EcsFilterInject<Inc<PlayerComponent, AiPlayerComponent>> _aiFilter = default;

        bool _subscribed;

        public void Init(IEcsSystems systems)
        {
            GameEventBus.Subscribe<CardCastEvent>(OnCast);
            _subscribed = true;
        }

        public void Dispose()
        {
            if (!_subscribed) return;
            GameEventBus.Unsubscribe<CardCastEvent>(OnCast);
            _subscribed = false;
        }

        void OnCast(CardCastEvent e)
        {
            if (!PveMode.Enabled) return;

            int card = e.CardEntity;
            if (card < 0 || !_ownerPool.Value.Has(card)) return;

            // Карта ИИ? (владелец = игрок с AiPlayerComponent)
            int aiId = -1;
            foreach (var pe in _aiFilter.Value) { aiId = _playerPool.Value.Get(pe).PlayerId; break; }
            if (aiId < 0 || _ownerPool.Value.Get(card).OwnerId != aiId) return;

            string cName = _modelPool.Value.Has(card) ? _modelPool.Value.Get(card).CardName : "";
            UnityEngine.Sprite cIcon = null;
            var visual = default(Game.Core.Shared.CardVisualData);

            if (_viewPool.Value.Has(card))
            {
                ref var v = ref _viewPool.Value.Get(card);
                cIcon = v.ArtImage;
                visual = new Game.Core.Shared.CardVisualData
                {
                    CardName    = v.CardName,
                    Description = v.Description,
                    Icon        = v.ArtImage,
                    CardType    = v.CardType,
                    Rarity      = v.Rarity,
                    Element     = v.Element,
                    CostType    = v.CostType,
                    CostAmount  = v.CostAmount,
                    IsCreature  = v.IsCreature,
                    Attack      = v.Attack,
                    MaxHealth   = v.MaxHealth,
                    Speed       = v.Speed,
                    IsCommander = v.IsCommander,
                };
            }

            GameEventBus.Publish(new OpponentCardPlayedUIEvent
            {
                CardName = cName,
                Icon     = cIcon,
                Visual   = visual,
            });
        }
    }
}
