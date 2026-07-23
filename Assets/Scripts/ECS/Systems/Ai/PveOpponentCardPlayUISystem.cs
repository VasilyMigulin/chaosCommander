using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Instance.Card;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// Показ «карта разыграна» (выкат карты) — на ЛЮБОЙ каст: свой, чужой (ИИ в PvE), авто-сгенерированный
    /// (Фокус-покус и т.п.), ловя CardCastEvent напрямую. Призывы попап не дают: они идут с NotCast и
    /// CardCastEvent не публикуют. В MP на ПАССИВНОМ клиенте CardCastEvent намеренно НЕ публикуется при
    /// реплее чужого хода (см. память project_build_debug_map.md, «СЧЁТЧИКИ И СИНК») — реплей оппонента
    /// там показывает попап отдельно, через ReplayActionSystem.ReplayCast; эта система его не дублирует,
    /// а покрывает «мой каст» (в т.ч. на активном MP-клиенте) + PvE (свой и ИИ) + любой авто-каст.
    /// Имя класса/файла — историческое (раньше было только для оппонента в PvE), намеренно не переименовано.
    /// </summary>
    public sealed class PveOpponentCardPlayUISystem : IEcsInitSystem, IEcsDestroySystem, System.IDisposable
    {
        readonly EcsWorldInject _world = default;
        readonly EcsPoolInject<OwnerComponent> _ownerPool = default;
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsPoolInject<CardModelComponent> _modelPool = default;
        readonly EcsPoolInject<CardViewDataComponent> _viewPool = default;
        readonly EcsFilterInject<Inc<PlayerComponent>> _playersFilter = default;

        bool _subscribed;

        public void Init(IEcsSystems systems)
        {
            GameEventBus.Subscribe<CardCastEvent>(OnCast);
            _subscribed = true;
        }

        // EcsSystems.Destroy() ищет IEcsDestroySystem, не System.IDisposable — без этого моста Dispose()
        // фреймворк никогда не вызывал бы (см. EcsRunHandler/TutorialEcsHandler.Dispose → _allSystems.Destroy()).
        public void Destroy(IEcsSystems systems) => Dispose();

        public void Dispose()
        {
            if (!_subscribed) return;
            GameEventBus.Unsubscribe<CardCastEvent>(OnCast);
            _subscribed = false;
        }

        void OnCast(CardCastEvent e)
        {
            int card = e.CardEntity;
            if (card < 0 || !_ownerPool.Value.Has(card)) return;

            string cName = _modelPool.Value.Has(card) ? _modelPool.Value.Get(card).CardName : "";
            UnityEngine.Sprite cIcon = null;
            var visual = default(Game.Core.Shared.CardVisualData);

            if (_viewPool.Value.Has(card))
            {
                ref var v = ref _viewPool.Value.Get(card);
                cIcon  = v.ArtImage;
                visual = CardVisualDataFactory.From(in v);
            }

            bool isLocal = IsLocalOwner(_ownerPool.Value.Get(card).OwnerId);

            GameEventBus.Publish(new OpponentCardPlayedUIEvent
            {
                CardName      = cName,
                Icon          = cIcon,
                Visual        = visual,
                IsLocalPlayer = isLocal,
            });
        }

        bool IsLocalOwner(int ownerId)
        {
            foreach (var pe in _playersFilter.Value)
            {
                ref var p = ref _playerPool.Value.Get(pe);
                if (p.PlayerId == ownerId) return p.IsLocalPlayer;
            }
            return false;
        }
    }
}
