using Game.Core.Ecs.Components;
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

        /// <summary>Prefab spawned on the board when this creature enters play.</summary>
        public GameObject ViewPrefab;

        /// <summary>«Умрёт через N своих ходов» ВШИТОЕ в модель (Сорняк и пр. токены-времянки). 0 = бессрочно.
        /// Зеркало CardCharmModel.TurnsAlive. Компонент вешается на ините, но CreatureTimerTickSystem тикает
        /// только существ НА БОРДЕ (Inc BoardTag) — до выхода на поле таймер инертен. Синк смерти — готовый
        /// путь таймера (TimerDeathNetEvent → ActionDeathData). Для «призванный умрёт через N» у SUMMON-эффектов
        /// по-прежнему модификатор DeathTimerEffect; это поле — для generate-токенов (модификаторов у них нет).</summary>
        public int TurnsAlive;

        /// <summary>Назначается сервисом сборки колоды когда эта карта выбрана командиром.</summary>
        [HideInInspector] public bool IsCommander;

        public override Game.Core.Service.EnumService.CardType GetCardType() => Game.Core.Service.EnumService.CardType.Creature;

        protected override void OnInit(EcsWorld world, int entityCard, bool isCommander)
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

            if (world.GetPool<CardViewDataComponent>().Has(entityCard))
            {
                ref var viewData    = ref world.GetPool<CardViewDataComponent>().Get(entityCard);
                viewData.IsCreature = true;
                viewData.Attack     = Attack;
                viewData.MaxHealth  = MaxHealth;
                viewData.Speed      = Speed;
                viewData.IsCommander = isCommander;
            }
        }
    }
}

