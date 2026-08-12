using System;
using System.Collections.Generic;
using Game.Core.Ecs.Components;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;
using UnityEngine;

namespace Game.Core.Ability
{
    // === class (OOP) ===
    /// <summary>
    /// N НЕЗАВИСИМЫХ активаций ОДНОЙ стадии-шаблона — в отличие от RepeatEffect (тот же Inner-эффект
    /// повторно на уже СОБРАННУЮ цель/цели), каждая активация здесь заново собирает кандидатов и выбирает
    /// цель ЗАНОВО: «нанести 3 урона случайному персонажу, повторить по разу за каждого Чёрта в этом
    /// матче» бьёт РАЗНЫХ случайных персонажей N раз, а не одного и того же N раз.
    ///
    /// Технически — тонкая обёртка над движком AbilityChain (RunChainSystem): N ОДИНАКОВЫХ ссылок на одну
    /// ChainStage-стадию (ChainStage — чистые данные без мутируемого рантайм-состояния, см. её докстринг —
    /// делить один инстанс между стадиями безопасно). Прогон стадий идёт как обычно: между активациями мир
    /// оседает (WorldSettled), Random выбирает заново из АКТУАЛЬНОГО списка кандидатов на каждом шаге.
    ///
    /// N — динамический (RepeatEffect.CountSource, тот же enum и тот же AbilityCount.Resolve, что и у
    /// RepeatEffect). Пересчитывается ЗАНОВО при КАЖДОМ новом срабатывании триггера (AbilityFire.Mark),
    /// а не один раз при OnInit — OnInit происходит на старте матча/создании карты, когда динамические
    /// счётчики («Чертей призвано в этом матче») ещё не отражают состояние на момент реального каста.
    /// </summary>
    public sealed class RepeatAbility : Ability
    {
        public ChainStage.TargetingMode Mode = ChainStage.TargetingMode.Target;
        public TargetSelection Selection = TargetSelection.Random;
        public int Count = 1;
        public TargetZone Zone = TargetZone.Board;
        public FieldArea Area = FieldArea.All;
        [SerializeReference] public List<ITargetFilter> Filters = new();

        public RepeatEffect.CountSource Source = RepeatEffect.CountSource.Fixed;
        public int FixedCount = 1;
        [Tooltip("Для MatchPlayedCard/MatchDrawnCard/MatchGenerated/MatchGeneratedSelf: ассет карты, чьи розыгрыши/взятия/генерации считаем.")]
        public ScriptableObject CountCard;
        [Tooltip("Для MatchArchetypeInvoked: архетип, чьи призывы считаем. Ключ берётся из него.")]
        [SerializeReference] public ICreatureTag Archetype;

        protected override void OnInit(EcsWorld world, int abilityEntity)
        {
            // Effects — базовый список (Ability.Effects): Init/условия/AbilityEffectContainerComponent уже
            // проставлены базовым Ability.Init ДО вызова OnInit. Ту же ссылку используем как эффекты стадии.
            var template = new ChainStage
            {
                Mode = Mode,
                Selection = Selection,
                Count = Count,
                Zone = Zone,
                Area = Area,
                Filters = Filters,
                Effects = Effects,
            };

            world.GetPool<RepeatAbilitySpecComponent>().Add(abilityEntity) = new RepeatAbilitySpecComponent
            {
                Template = template,
                Source = Source,
                FixedCount = FixedCount,
                CountCard = CountCard,
                Archetype = Archetype,
            };

            // Stages пуст до первого AbilityFire.Mark — он пересчитает N и наполнит массив ПЕРЕД тем, как
            // RunCheckAbilityRulesSystem поставит ChainStateComponent (см. Mark).
            world.GetPool<AbilityChainComponent>().Add(abilityEntity).Stages = Array.Empty<ChainStage>();
        }
    }

    // === struct (Component) ===
    /// <summary>Шаблон стадии + настройки динамического счёта для RepeatAbility. Ставит RepeatAbility.OnInit
    /// (один раз), читает и пересчитывает AbilityFire.Mark (на каждое новое срабатывание триггера) —
    /// оба в Game.Core.Ability, наружу (Ecs.Systems) этот компонент не нужен: тот видит только
    /// AbilityChainComponent/ChainStateComponent, которые Mark уже наполнил свежими Stages.</summary>
    public struct RepeatAbilitySpecComponent
    {
        public ChainStage Template;
        public RepeatEffect.CountSource Source;
        public int FixedCount;
        public ScriptableObject CountCard;
        public ICreatureTag Archetype;
    }
}
