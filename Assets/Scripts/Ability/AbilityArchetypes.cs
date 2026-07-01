using System;
using System.Collections.Generic;
using Game.Core.Ecs.Components;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;

namespace Game.Core.Ability
{
    // ─────────────────────────────────────────────────────────────────────────
    // АРХЕТИПЫ (работяги/черти/...). [SerializeReference]-объекты в CardModel.Archetypes; на ините каждый
    // САМ вешает свой ключ в общий ArchetypeComponent (без per-архетип ECS-структ и без switch). Тот же
    // объект проверяет наличие (Has) и отдаёт Key — это переиспользуют ArchetypeTargetFilter и счётчики.
    // Новый архетип = класс-наследник ArchetypeTag с уникальным Key (одна строка). Имена классов сохранены
    // (WorkerArchetype/ImpArchetype) — на них ссылаются ассеты существ через [SerializeReference].
    // ─────────────────────────────────────────────────────────────────────────

    public abstract class ArchetypeTag : ICreatureTag
    {
        public abstract string Key { get; }

        public void Apply(EcsWorld world, int cardEntity)
        {
            var p = world.GetPool<ArchetypeComponent>();
            if (!p.Has(cardEntity)) p.Add(cardEntity).Keys = new List<string>();
            ref var a = ref p.Get(cardEntity);
            a.Keys ??= new List<string>();
            if (!a.Keys.Contains(Key)) a.Keys.Add(Key);
        }

        public bool Has(EcsWorld world, int entity)
        {
            var p = world.GetPool<ArchetypeComponent>();
            return p.Has(entity) && p.Get(entity).Keys != null && p.Get(entity).Keys.Contains(Key);
        }
    }

    [Serializable] public sealed class WorkerArchetype : ArchetypeTag { public override string Key => "Worker"; }
    [Serializable] public sealed class ImpArchetype    : ArchetypeTag { public override string Key => "Imp"; }
}
