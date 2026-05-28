using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using Game.Core.Ecs.Components;
using Game.Core.Mono;
using UnityEngine;

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
        readonly EcsCustomInject<BoardView> _boardView = default;

        readonly EcsFilterInject<
            Inc<BoardTag, CreatureTag, BoardPositionComponent, ViewRefComponent>,
            Exc<ViewSpawnedTag>>
            _filter = default;

        readonly EcsPoolInject<BoardPositionComponent> _posPool     = default;
        readonly EcsPoolInject<ViewRefComponent>       _viewPool    = default;
        readonly EcsPoolInject<ViewSpawnedTag>         _spawnedPool = default;
        readonly EcsPoolInject<OwnerComponent>         _ownerPool   = default;

        public void Run(IEcsSystems systems)
        {
            foreach (var entity in _filter.Value)
            {
                ref var vr  = ref _viewPool.Value.Get(entity);
                ref var pos = ref _posPool.Value.Get(entity);

                if (vr.Prefab == null)
                {
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

                // Заменяем prefab-ссылку ссылкой на инстанс
                vr.View = instance;

                // Помечаем что view уже создан
                _spawnedPool.Value.Add(entity);

                Debug.Log($"[SpawnCreatureViewSystem] Spawned creature view for entity={entity} at row={pos.Row} col={pos.Col} ownerId={pos.OwnerId}");
            }
        }
    }
}
