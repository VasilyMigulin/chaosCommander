using System.Collections.Generic;
using Game.Core.Ecs.Components;
using Game.Core.Shared.Interface;
using Leopotam.EcsLite;
using UnityEngine;

namespace Game.Core.Model.Card.Creature
{
    /// <summary>
    /// Data model for creature cards.
    /// Played using Gold resource. Occupies a board cell.
    /// </summary>
    public class CardCreatureModel : CardModel
    {
        public int MaxHealth;
        public int Attack;

        /// <summary>Actions per turn (move + attack budget).</summary>
        public int Speed;

        /// <summary>Свойства существа (Двойной удар/Укреплённый/...) — кейворды, НЕ Ability. Каждое само
        /// вешает свой ECS-компонент на ините (ICreatureProperty.Apply). Пусто = без свойств. Раздать
        /// свойство РАНТАЙМОМ (спелл/аура) — PropertyBuff{Property} + AddBuffEffect, см. AbilityProperties.cs.</summary>
        [SerializeReference] public List<ICreatureProperty> Properties = new List<ICreatureProperty>();

        /// <summary>Prefab spawned on the board when this creature enters play.</summary>
        public GameObject ViewPrefab;

        /// <summary>Косметика ПОЯВЛЕНИЯ на столе (Pre-портал на клетке / Resolve-аура на существе /
        /// Finish-аккорд) — эпичный вход лег/экзотов; см. SummonVfxSpec. Пустая спека — обычное появление.</summary>
        public Game.Core.Shared.Interface.SummonVfxSpec SummonVfx;

        /// <summary>«Умрёт через N своих ходов» ВШИТОЕ в модель (Сорняк и пр. токены-времянки). 0 = бессрочно.
        /// Зеркало CardCharmModel.TurnsAlive. Компонент вешается на ините, но CreatureTimerTickSystem тикает
        /// только существ НА БОРДЕ (Inc BoardTag) — до выхода на поле таймер инертен. Синк смерти — готовый
        /// путь таймера (TimerDeathNetEvent → ActionDeathData). Для «призванный умрёт через N» у SUMMON-эффектов
        /// по-прежнему модификатор DeathTimerEffect; это поле — для generate-токенов (модификаторов у них нет).</summary>
        public int TurnsAlive;

        /// <summary>Назначается сервисом сборки колоды когда эта карта выбрана командиром.</summary>
        [HideInInspector] public bool IsCommander;

        public override Game.Core.Service.EnumService.CardType GetCardType() => Game.Core.Service.EnumService.CardType.Creature;

        protected override void OnInit(EcsWorld world, int entityCard, int playerOwnerEntity, bool isCommander)
        {
            ref var atk = ref world.GetPool<AttackComponent>().Add(entityCard);
            atk.Value = Attack;
            atk.Base  = Attack;

            ref var hp = ref world.GetPool<HealthComponent>().Add(entityCard);
            hp.Max     = MaxHealth;
            hp.BaseMax = MaxHealth;
            hp.Current = MaxHealth;

            world.GetPool<CreatureTag>().Add(entityCard);

            if (isCommander)
                world.GetPool<CommanderTag>().Add(entityCard);

            world.GetPool<ViewRefComponent>().Add(entityCard).Prefab = ViewPrefab;

            ref var speed = ref world.GetPool<SpeedComponent>().Add(entityCard);
            speed.BaseMax   = Speed;
            speed.Max       = Speed;
            speed.Remaining = Speed;

            if (TurnsAlive > 0)
                world.GetPool<CreatureTimerComponent>().Add(entityCard).TurnsRemaining = TurnsAlive;

            if (Properties != null)
                foreach (var prop in Properties)
                    prop?.Apply(world, entityCard);

            if (SummonVfx != null && SummonVfx.HasAny)
                world.GetPool<SummonVfxComponent>().Add(entityCard).Spec = SummonVfx;

            if (world.GetPool<CardViewDataComponent>().Has(entityCard))
            {
                ref var viewData    = ref world.GetPool<CardViewDataComponent>().Get(entityCard);
                viewData.IsCreature = true;
                viewData.Attack     = Attack;
                viewData.MaxHealth  = MaxHealth;
                viewData.Speed      = Speed;
                viewData.IsCommander = isCommander;

                // Свойства (Двойной удар/Защитник/...) — ярлык-кейворд ВСЕГДА в начале описания, вне
                // авторского текста карты. Базовый CardModel.Init уже собрал viewData.Description без них
                // (Properties — поле CardCreatureModel, база о нём не знает) — приклеиваем префикс здесь же.
                // Тот же путь (ReapplyTypeInit → OnInit) гоняет и RunTransformSystem — полиморф пересчитает
                // ярлыки под НОВУЮ модель тоже, бесплатно.
                if (Properties != null && Properties.Count > 0)
                {
                    var keys = new List<string>(Properties.Count);
                    foreach (var prop in Properties)
                        if (prop != null) keys.Add(prop.Key);
                    viewData.Description = Game.Core.Shared.CardDescriptionFormatter.BuildPropertyPrefix(keys) + viewData.Description;
                }
            }
        }
    }
}

