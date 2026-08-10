using Game.Core.Ecs.Components;
using Game.Core.Mono;
using Leopotam.EcsLite;
using UnityEngine;

namespace Game.Core.Ecs.Systems
{
    /// <summary>
    /// Мировая позиция сущности для «откуда должна лететь визуальная анимация» (карта в руку и т.п.):
    /// клетка борда → инстанс вью (существо-источник ещё жив) → аватар владельца. В отличие от похожего
    /// хелпера в RunResolveAbilityQueueSystem (тот всегда отдаёт ХОТЬ ЧТО-ТО, с фолбэком на центр доски —
    /// нужен для VFX-таргетинга), здесь false означает «источника нет» (обычный добор/дискавер) — вызывающий
    /// использует свой дефолт (анимация «из-за края экрана»), а не летит из случайной точки на столе.
    /// Живёт в Game.Core.Ecs.Systems (не Game.Core.Mono): нужен EcsWorld/Leopotam, а Mono-сборка ECS не
    /// референсит (однонаправленная граница ECS→Mono, не наоборот).
    /// </summary>
    public static class EntityWorldPosUtil
    {
        public static bool TryGet(EcsWorld world, BoardView bv, int entity, out Vector3 pos)
        {
            pos = default;
            if (bv == null || entity < 0) return false;

            var posPool = world.GetPool<BoardPositionComponent>();
            if (posPool.Has(entity))
            {
                ref var p = ref posPool.Get(entity);
                var cell = bv.GetCell(p.Row, p.Col, p.OwnerId);
                if (cell != null) { pos = cell.transform.position; return true; }
            }

            var viewPool = world.GetPool<ViewRefComponent>();
            if (viewPool.Has(entity) && viewPool.Get(entity).View != null)
            {
                pos = viewPool.Get(entity).View.transform.position;
                return true;
            }

            var ownerPool = world.GetPool<OwnerComponent>();
            if (ownerPool.Has(entity))
            {
                var ac = bv.GetAvatarCell(ownerPool.Get(entity).OwnerId);
                if (ac != null) { pos = ac.transform.position; return true; }
            }

            // Сама сущность — ИГРОК (аватар): у неё нет OwnerComponent (см. комментарий в TakeDamageSystem),
            // только PlayerComponent. Без этой ветки VFX «на аватаре владельца» (Вампиризм и т.п.) молча
            // не резолвился бы для игрока напрямую — только для существ через их OwnerComponent выше.
            var playerPool = world.GetPool<PlayerComponent>();
            if (playerPool.Has(entity))
            {
                var ac = bv.GetAvatarCell(playerPool.Get(entity).PlayerId);
                if (ac != null) { pos = ac.transform.position; return true; }
            }

            return false;
        }
    }
}
