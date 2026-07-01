using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Service;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Тех-читы для тестов: по DebugFillResourcesEvent выдаёт ЛОКАЛЬНОМУ игроку максимум маны и золота.
    /// Локальное действие (ресурсы оппонента на пассиве косметичны, реплей их не валидирует) → синк не нужен.
    /// Инертна без события (его шлёт только дев-оверлей).
    /// </summary>
    public sealed class DebugCheatSystem : IEcsInitSystem, IEcsRunSystem, IEcsDestroySystem
    {
        readonly EcsPoolInject<PlayerComponent> _playerPool = default;
        readonly EcsPoolInject<ManaComponent>   _manaPool   = default;
        readonly EcsPoolInject<GoldComponent>   _goldPool   = default;
        readonly EcsFilterInject<Inc<PlayerComponent>> _players = default;

        bool _fill;

        public void Init(IEcsSystems systems) => GameEventBus.Subscribe<DebugFillResourcesEvent>(OnFill);
        public void Destroy(IEcsSystems systems) => GameEventBus.Unsubscribe<DebugFillResourcesEvent>(OnFill);

        void OnFill(DebugFillResourcesEvent _) => _fill = true;

        const int Big = 99;   // потолок ресурсов в чите: видимое изменение + хватает на дорогие карты

        public void Run(IEcsSystems systems)
        {
            if (!_fill) return;
            _fill = false;

            int affected = 0;
            foreach (var pe in _players.Value)
            {
                if (!_playerPool.Value.Get(pe).IsLocalPlayer) continue;
                affected++;

                bool hasMana = _manaPool.Value.Has(pe);
                bool hasGold = _goldPool.Value.Has(pe);

                if (hasMana)
                {
                    ref var m = ref _manaPool.Value.Get(pe);
                    if (m.Max < Big) m.Max = Big;   // поднимаем и потолок — иначе при уже полном ресурсе изменения не видно
                    m.Current = m.Max;
                    GameEventBus.Publish(new ResourceChangedEvent
                    {
                        isLocalPlayer = true, Type = EnumService.ResourceType.Mana, NewValue = m.Current, MaxValue = m.Max,
                    });
                }
                if (hasGold)
                {
                    ref var g = ref _goldPool.Value.Get(pe);
                    if (g.Max < Big) g.Max = Big;
                    g.Current = g.Max;
                    GameEventBus.Publish(new ResourceChangedEvent
                    {
                        isLocalPlayer = true, Type = EnumService.ResourceType.Gold, NewValue = g.Current, MaxValue = g.Max,
                    });
                }

                UnityEngine.Debug.Log($"[DebugCheat] FillResources: player={pe} mana={hasMana} gold={hasGold} → {Big}");
            }

            if (affected == 0)
                UnityEngine.Debug.LogWarning("[DebugCheat] FillResources: локальный игрок не найден (IsLocalPlayer=false у всех). ECS-бой запущен?");
        }
    }
}
