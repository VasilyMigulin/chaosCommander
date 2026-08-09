using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Mono;
using UnityEngine;
using SummonVfxSpec = Game.Core.Shared.Interface.SummonVfxSpec;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Спавнит визуальный GameObject существа на доске.
    /// Срабатывает один раз: когда на сущности появляется BoardTag + ViewRefComponent,
    /// но ещё нет флага ViewSpawnedTag (нет инстанса).
    /// После спауна заменяет ViewRefComponent.View ссылкой на инстанс.
    /// </summary>
    public sealed class SpawnCreatureViewSystem : IEcsRunSystem
    {
        // ВРЕМЕННО ВЫКЛЮЧЕНО (2026-08-06): автоскейл под клетку (CreatureView.FitToBounds) даёт разный
        // размер у визуально одинаковых моделей — похоже на баг с bind-pose/риггингом, разбирается отдельно.
        // true — вернуть как было.
        const bool AutoScaleEnabled = false;

        readonly EcsCustomInject<BoardView> _boardView = default;
        readonly EcsCustomInject<Game.Core.Configs.DefaultAbilityVfxConfig> _defaultVfx = default;

        readonly EcsFilterInject<
            Inc<BoardTag, CreatureTag, BoardPositionComponent, ViewRefComponent>,
            Exc<ViewSpawnedTag>>
            _filter = default;

        readonly EcsPoolInject<BoardPositionComponent> _posPool     = default;
        readonly EcsPoolInject<ViewRefComponent>       _viewPool    = default;
        readonly EcsPoolInject<ViewSpawnedTag>         _spawnedPool = default;
        readonly EcsPoolInject<OwnerComponent>         _ownerPool   = default;
        readonly EcsPoolInject<ViewSpawnFromComponent> _spawnFromPool = default;
        readonly EcsPoolInject<SummonVfxComponent>     _summonVfxPool = default;

        // Дефолтная спека «пыль на клетке» для существ БЕЗ собственной SummonVfx (кэш — не плодим по объекту
        // на каждый спавн). Собирается лениво из DefaultAbilityVfxConfig.DefaultSummonVfxPrefab.
        SummonVfxSpec _defaultSummonSpec;

        public void Run(IEcsSystems systems)
        {
            foreach (var entity in _filter.Value)
            {
                ref var vr  = ref _viewPool.Value.Get(entity);
                ref var pos = ref _posPool.Value.Get(entity);

                if (vr.Prefab == null)
                {
                    Debug.LogWarning($"[SpawnCreatureViewSystem] entity={entity} (row={pos.Row} col={pos.Col} owner={pos.OwnerId}) " +
                                     "БЕЗ ViewRefComponent.Prefab → вид не создан. Проверь ViewPrefab у CardCreatureModel токена.");
                    _spawnedPool.Value.Add(entity);
                    continue;
                }

                // Получаем мировую позицию клетки через BoardView
                var cell = _boardView.Value.GetCell(pos.Row, pos.Col, pos.OwnerId);
                Vector3 spawnPos = cell != null
                    ? cell.transform.position
                    : new Vector3(pos.Col, 0f, pos.Row);

                // Спавним инстанс
                var instance = Object.Instantiate(vr.Prefab, spawnPos, Quaternion.identity);
                instance.name = $"Creature_E{entity}";

                var creatureView = instance.GetComponent<CreatureView>();
                creatureView?.SetCell(pos.Row, pos.Col, pos.OwnerId);
                if (AutoScaleEnabled)
                    creatureView?.FitToBounds(cell?.FitBounds);   // автоскейл под клетку — ДО поворота/поп-анимации
                creatureView?.SetOwnerFacing(pos.OwnerId);   // владелец 2 смотрит на оппонента

                // Спека появления (Pre-портал/Resolve-аура/Finish-аккорд): своя с CardCreatureModel
                // (леги/экзоты) или дефолтная «пыль на клетке» из DefaultAbilityVfxConfig (все остальные).
                var summonVfx = _summonVfxPool.Value.Has(entity)
                    ? _summonVfxPool.Value.Get(entity).Spec
                    : DefaultSummonSpec();

                // Драг-розыгрыш: вью «въезжает» из точки под пальцем (ViewSpawnFrom) в клетку + Invoke.
                // Иначе — обычное появление с поп-масштабом (PlaySummon). Оба держат IsSummoning (гейт OnCast).
                if (_spawnFromPool.Value.Has(entity))
                {
                    Vector3 from = _spawnFromPool.Value.Get(entity).From;
                    _spawnFromPool.Value.Del(entity);
                    creatureView?.PlaySlideInvoke(from, spawnPos, summonVfx);
                }
                else
                {
                    creatureView?.PlaySummon(summonVfx);      // анимация появления (#2, оба клиента); OnCast ждёт её
                }

                // Заменяем prefab-ссылку ссылкой на инстанс
                vr.View = instance;

                // Помечаем что view уже создан
                _spawnedPool.Value.Add(entity);
            }
        }

        // null, если дефолтная пыль не настроена в конфиге (обычное появление без эффекта).
        SummonVfxSpec DefaultSummonSpec()
        {
            var prefab = _defaultVfx.Value != null ? _defaultVfx.Value.DefaultSummonVfxPrefab : null;
            if (prefab == null) return null;
            if (_defaultSummonSpec == null || _defaultSummonSpec.PrePrefab != prefab)
                _defaultSummonSpec = new SummonVfxSpec { PrePrefab = prefab };
            return _defaultSummonSpec;
        }
    }
}
