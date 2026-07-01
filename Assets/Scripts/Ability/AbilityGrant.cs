using System;
using Game.Core.Ecs.Components;
using Leopotam.EcsLite;
using UnityEngine;

namespace Game.Core.Ability
{
    // === class (OOP) === Навесить ЛЮБУЮ способность на цель-сущность: клонирует шаблон, создаёт ability-
    // сущность, инитит её (target = карта-владелец способности) и дописывает в AbilityContainerComponent цели.
    // Универсально → разные комбо: Газовое вздутие (на существ оппонента OnDie+ShuffleCardIntoDeck{облако});
    // навесить бафф-триггер и т.п. Индекс = текущая длина контейнера цели → детерминированно: оба клиента
    // дописывают на ОДИН индекс (контейнеры зеркальны), а сработавшая способность синкается обычным
    // ActionAbilityData по этому индексу. Применяется на обоих (резолв реплеится) → способность есть у обоих.
    [Serializable]
    public sealed class AddAbilityEffect : EffectBase
    {
        [SerializeReference] public Ability Granted;

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (Granted == null || target < 0) return;

            int targetPlayer = OwnerPlayer(world, target);
            var contPool = world.GetPool<AbilityContainerComponent>();
            int index = (contPool.Has(target) && contPool.Get(target).AbilityEntities != null)
                ? contPool.Get(target).AbilityEntities.Length : 0;

            // склонировать шаблон + своя ability-сущность + инит (триггеры подпишутся)
            var clone = (Ability)Granted.DeepClone();
            int abilityEntity = world.NewEntity();
            world.GetPool<AbilityRefComponent>().Add(abilityEntity).Ability = clone;
            clone.Init(world, abilityEntity, target, targetPlayer, index);

            // ИНИЦИАТОР гранёной способности: если этот грант сам идёт под инициатором (вложенный грант) —
            // наследуем его, иначе инициатор = владелец карты-грантёра (Газовое вздутие → ты). Нужно, чтобы
            // генерация из гранёного эффекта (облако в чужую колоду) атрибутировалась тебе, а не оппоненту.
            int origin = AbilityResolveContext.OriginOwnerId >= 0
                ? AbilityResolveContext.OriginOwnerId
                : OwnerId(world, cardEntity);
            if (origin >= 0) world.GetPool<AbilityOriginComponent>().Add(abilityEntity).OriginOwnerId = origin;

            // дописать в контейнер цели (свежий ref после Init)
            if (!contPool.Has(target)) contPool.Add(target);
            ref var cont = ref contPool.Get(target);
            var arr = cont.AbilityEntities ?? Array.Empty<int>();
            var next = new int[arr.Length + 1];
            Array.Copy(arr, next, arr.Length);
            next[arr.Length] = abilityEntity;
            cont.AbilityEntities = next;
        }

        static int OwnerId(EcsWorld world, int entity)
        {
            var ownerPool = world.GetPool<OwnerComponent>();
            return ownerPool.Has(entity) ? ownerPool.Get(entity).OwnerId : -1;
        }

        static int OwnerPlayer(EcsWorld world, int entity)
        {
            var ownerPool = world.GetPool<OwnerComponent>();
            if (!ownerPool.Has(entity)) return -1;
            int ownerId = ownerPool.Get(entity).OwnerId;
            var pp = world.GetPool<PlayerComponent>();
            foreach (var pe in world.Filter<PlayerComponent>().End())
                if (pp.Get(pe).PlayerId == ownerId) return pe;
            return -1;
        }
    }
}
