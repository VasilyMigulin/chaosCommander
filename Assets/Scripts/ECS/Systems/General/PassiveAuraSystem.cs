using System.Collections.Generic;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Events;
using Game.Core.Shared.Interface;

namespace Game.Core.Ecs.Systems
{
    // === class (ECS system) ===
    /// <summary>
    /// ПАССИВНЫЕ АУРЫ ОТ КАРТ (PassiveAuraComponent, ставит PassiveAuraEffect): «пока карта в зоне
    /// SourceZone — цели по Filters имеют Buff». «Шальной десница»: пока в РУКЕ, другие свои существа
    /// получают +1 атаки / +1 скорости.
    ///
    /// РЕАКТИВНО (не по-кадрово, как того требует архитектура проекта — см. BuffPerCharmSystem): события
    /// лишь взводят dirty-флаг, один дифф-проход в ближайшем Run. Дифф идёт по СОСТОЯНИЮ: желаемый набор
    /// целей (Gather по фильтрам, если источник в нужной зоне) сравнивается с уже выданными (Applied) —
    /// новым выдаём, выбывшим снимаем. Поэтому «существо вышло после розыгрыша ауры» и «карта покинула
    /// руку» обрабатываются сами, без отдельных триггеров.
    ///
    /// СИНК ДАРОМ: зоны и борд зеркальны у обоих клиентов, дифф локальный по одинаковым данным.
    /// Регистрация: _generalSystems, рядом с BuffPerCharmSystem (после смертей — выбывшие уже вышли).
    /// </summary>
    public sealed class PassiveAuraSystem : IEcsInitSystem, IEcsRunSystem, IEcsDestroySystem, System.IDisposable
    {
        readonly EcsWorldInject _world = default;
        readonly EcsFilterInject<Inc<PassiveAuraComponent>> _auras = default;
        readonly EcsPoolInject<PassiveAuraComponent> _auraPool = default;

        readonly EcsPoolInject<HandTag>  _handPool  = default;
        readonly EcsPoolInject<BoardTag> _boardPool = default;
        readonly EcsPoolInject<DeckTag>  _deckPool  = default;
        readonly EcsPoolInject<GraveTag> _gravePool = default;
        readonly EcsPoolInject<DeadTag>  _deadPool  = default;
        readonly EcsPoolInject<CommanderCooldownComponent> _cooldownPool = default;

        bool _dirty = true;   // старт матча: применить начальное состояние
        bool _subscribed;

        public void Init(IEcsSystems systems)
        {
            if (_subscribed) return;
            _subscribed = true;
            GameEventBus.Subscribe<CreatureInvokedEvent>(OnAny);   // новое существо на столе → цель появилась
            GameEventBus.Subscribe<CreatureDiedEvent>(OnAny);      // цель/источник умерли
            GameEventBus.Subscribe<CardPlayedEvent>(OnAny);        // источник разыгран (покинул руку)
            GameEventBus.Subscribe<CardDrawnEvent>(OnAny);         // источник добран (пришёл в руку)
            GameEventBus.Subscribe<CardDiscardedEvent>(OnAny);     // источник сброшен
            GameEventBus.Subscribe<CardGeneratedEvent>(OnAny);     // источник/цель созданы эффектом
            GameEventBus.Subscribe<TurnStartedEvent>(OnAny);       // страховка: пути без доменного события
        }

        public void Destroy(IEcsSystems systems) => Dispose();

        public void Dispose()
        {
            if (!_subscribed) return;
            _subscribed = false;
            GameEventBus.Unsubscribe<CreatureInvokedEvent>(OnAny);
            GameEventBus.Unsubscribe<CreatureDiedEvent>(OnAny);
            GameEventBus.Unsubscribe<CardPlayedEvent>(OnAny);
            GameEventBus.Unsubscribe<CardDrawnEvent>(OnAny);
            GameEventBus.Unsubscribe<CardDiscardedEvent>(OnAny);
            GameEventBus.Unsubscribe<CardGeneratedEvent>(OnAny);
            GameEventBus.Unsubscribe<TurnStartedEvent>(OnAny);
        }

        void OnAny(CreatureInvokedEvent _) => _dirty = true;
        void OnAny(CreatureDiedEvent _)    => _dirty = true;
        void OnAny(CardPlayedEvent _)      => _dirty = true;
        void OnAny(CardDrawnEvent _)       => _dirty = true;
        void OnAny(CardDiscardedEvent _)   => _dirty = true;
        void OnAny(CardGeneratedEvent _)   => _dirty = true;
        void OnAny(TurnStartedEvent _)     => _dirty = true;

        static readonly List<int> _toRemove = new();

        public void Run(IEcsSystems systems)
        {
            if (!_dirty) return;
            _dirty = false;

            var world = _world.Value;

            foreach (var source in _auras.Value)
            {
                ref var aura = ref _auraPool.Value.Get(source);
                if (aura.Buff == null) continue;
                aura.Applied ??= new List<int>();

                // Аура активна, пока источник в своей зоне, жив и НЕ НА КУЛДАУНЕ (общее правило: командир
                // после гибели ждёт хода доступности — его способности, включая ауры, отключены).
                bool active = !_deadPool.Value.Has(source)
                              && !_cooldownPool.Value.Has(source)
                              && InZone(source, aura.SourceZone);

                // casterPlayer ОБЯЗАТЕЛЕН: селекторы «свой/чужой» (AllyTargetFilter и др.) считают сторону
                // ОТНОСИТЕЛЬНО СУЩНОСТИ ИГРОКА-кастера. С -1 ни одна цель не проходила фильтр — аура молча
                // не работала (баг 2026-07-30 «у Шального десницы не изменились статы»).
                // includeCommanderInZones: аура — БЛАГОТВОРНАЯ выборка, командир в руке/зоне обязан
                // попадать под неё (на столе он и так попадает). Иначе командир-в-руке молча без баффа.
                var wanted = active
                    ? TargetGather.Gather(world, aura.Filters, source, OwnerPlayerEntity(world, source), null,
                                          aura.TargetZone, includeCommanderInZones: true)
                    : null;

                // 1) Снять с тех, кто выбыл (умер/ушёл с борда/аура выключилась).
                _toRemove.Clear();
                foreach (var applied in aura.Applied)
                    if (wanted == null || !wanted.Contains(applied))
                        _toRemove.Add(applied);

                foreach (var t in _toRemove)
                {
                    aura.Buff.Revert(world, source, t);
                    aura.Applied.Remove(t);
                }

                // 2) Выдать новым (в наборе, но ещё без баффа).
                if (wanted == null) continue;
                foreach (var t in wanted)
                {
                    if (aura.Applied.Contains(t)) continue;
                    aura.Buff.Apply(world, source, t);
                    aura.Applied.Add(t);
                }
            }
        }

        // Сущность игрока-владельца карты (для caster-relative фильтров). -1, если владелец не найден.
        int OwnerPlayerEntity(EcsWorld world, int cardEntity)
        {
            var ownerPool = world.GetPool<OwnerComponent>();
            if (!ownerPool.Has(cardEntity)) return -1;
            int ownerId = ownerPool.Get(cardEntity).OwnerId;
            var playerPool = world.GetPool<PlayerComponent>();
            foreach (var pe in world.Filter<PlayerComponent>().End())
                if (playerPool.Get(pe).PlayerId == ownerId) return pe;
            return -1;
        }

        bool InZone(int entity, TargetZone zone)
        {
            switch (zone)
            {
                case TargetZone.Hand:  return _handPool.Value.Has(entity);
                case TargetZone.Board: return _boardPool.Value.Has(entity);
                case TargetZone.Deck:  return _deckPool.Value.Has(entity);
                case TargetZone.Grave: return _gravePool.Value.Has(entity);
                default:               return true;   // Any — аура всегда активна
            }
        }
    }
}
