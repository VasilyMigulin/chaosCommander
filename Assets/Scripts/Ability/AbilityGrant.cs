using System;
using Game.Core.Ecs.Components;
using Game.Core.Events;
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

    // === class (OOP) === Клонирует ВСЕ ЖИВЫЕ способности сущности SourceEntity на target — не путать с
    // AddAbilityEffect (тот вешает ОДИН фиксированный, авторский-на-ассете шаблон). Этот собирается ИЗ КОДА
    // (не сериализуется, SourceEntity задаётся конструктором/полем прямо перед Apply) — нужен там, где набор
    // способностей заранее неизвестен: SummonAllPlayedCharmsEffect («Мистер Постоянство») пересоздаёт чару из
    // журнала по ModelId (голый печатный шаблон), но если исходник — карта, собранная графтом («Проклятье
    // для принцессы»: несколько раскопок донор-эффектов клонируют способности ПРЯМО на сущность, без своего
    // ModelId), пересозданная копия иначе осталась бы пустой болванкой. Базовые тир-токены СВОИХ способностей
    // не имеют (RuntimeAbilities: [] — весь функционал донора-графта), поэтому дописываем БЕЗ риска задвоить.
    public sealed class CloneEntityAbilitiesEffect : EffectBase
    {
        public int SourceEntity = -1;

        public override void Apply(EcsWorld world, int cardEntity, int target)
        {
            if (SourceEntity < 0 || target < 0) return;

            // Свежий спавн из CardConfig наследует IsToken АССЕТА (обычно 1 у донор-болванок графта, иначе
            // они попадали бы в случайные пулы) → CardModel.Init повесил TokenTag. Но раз запись вообще
            // попала в CharmsPlayedLog, источник УЖЕ был не-токеном на момент розыгрыша (MatchCounterTrackerSystem
            // фильтрует токены при записи) — значит и копия не токен, иначе PlayerStatsViewSystem.GatherAuras
            // (charm-бар, Exc<TokenTag>) её не покажет: «Мистер Постоянство не создаёт дубль» — на деле создаёт,
            // просто дубль невидим в баре (баг 2026-08-21).
            var tokenPool = world.GetPool<TokenTag>();
            if (tokenPool.Has(target)) tokenPool.Del(target);

            var containerPool = world.GetPool<AbilityContainerComponent>();
            if (!containerPool.Has(SourceEntity)) return;

            var srcEntities = containerPool.Get(SourceEntity).AbilityEntities;
            if (srcEntities == null || srcEntities.Length == 0) return;

            int targetPlayer = OwnerPlayer(world, target);
            var refPool = world.GetPool<AbilityRefComponent>();
            if (!containerPool.Has(target)) containerPool.Add(target).AbilityEntities = Array.Empty<int>();
            ref var destContainer = ref containerPool.Get(target);
            var merged = new System.Collections.Generic.List<int>(destContainer.AbilityEntities ?? Array.Empty<int>());
            int index = merged.Count;

            foreach (var srcAbilityEntity in srcEntities)
            {
                if (!refPool.Has(srcAbilityEntity)) continue;
                var clone = (Ability)AbilityCloneUtil.DeepClone(refPool.Get(srcAbilityEntity).Ability);
                int newAbilityEntity = world.NewEntity();
                refPool.Add(newAbilityEntity).Ability = clone;
                clone.Init(world, newAbilityEntity, target, targetPlayer, index++);
                merged.Add(newAbilityEntity);
            }
            destContainer.AbilityEntities = merged.ToArray();

            // Смёрженный графтом текст донор-эффектов — та же болезнь, что чинили в GraftAbilitiesFromTargetEffect
            // (баг 2026-08-21): без этого пересозданная копия визуально выглядит пустой болванкой базового
            // тира, хотя способности у нёе уже есть. Берём у ИСТОЧНИКА весь текст КРОМЕ его собственной
            // последней строки (та — «Действует N ходов» под ЕГО текущий, возможно уже подтикавший таймер) и
            // приклеиваем перед СВОЕЙ (правильной, свежерасчитанной Init'ом) последней строкой у target.
            var viewPool = world.GetPool<CardViewDataComponent>();
            if (viewPool.Has(SourceEntity) && viewPool.Has(target))
            {
                string srcDesc = viewPool.Get(SourceEntity).Description;
                if (!string.IsNullOrEmpty(srcDesc))
                {
                    ref var destView = ref viewPool.Get(target);
                    int srcLastNl = srcDesc.LastIndexOf('\n');
                    string srcBody = srcLastNl >= 0 ? srcDesc.Substring(0, srcLastNl) : srcDesc;

                    string destDesc = destView.Description ?? string.Empty;
                    int destLastNl = destDesc.LastIndexOf('\n');
                    string destTail = destLastNl >= 0 ? destDesc.Substring(destLastNl) : ("\n" + destDesc);

                    destView.Description = srcBody + destTail;
                    GameEventBus.Publish(new Game.Core.Events.CardDescriptionChangedUIEvent
                        { CardEntity = target, Description = destView.Description });
                }
            }
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
