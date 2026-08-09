using System.Collections.Generic;
using Game.Core.Service;
using UnityEngine;
using AbilityRuntime = Game.Core.Ability.Ability;

namespace Game.Core.Model.Card
{
    /// <summary>Источник, по которому карта-с-уровнями определяет текущий уровень (чем повышается). Значения
    /// закреплены явно (как CountSource) — перестановка ломает заавторенные ассеты. Расширяемо.</summary>
    public enum TierSource
    {
        None    = 0,
        GoldMax = 1,   // максимум золота владельца (доход, GoldComponent.Max) — растёт по ходам
        ManaMax = 2,   // максимум маны владельца (ManaComponent.Max) — по аналогии с золотом
        Corpses = 3,   // «трупы» владельца: существа в ЕГО кладбище СЕЙЧАС + его токены, погибшие за матч
                       // (токены в кладбище не попадают — Limbo, трекаются счётчиком MatchCounter.TokensDied)
    }

    /// <summary>
    /// Один УРОВЕНЬ карты-с-уровнями — надстройка над моделью карты (CardModel.Tiers). Переопределяет статы
    /// (Attack/Health/Speed), стоимость (PlayCost) и НАБОР способностей. Активируется, когда значение
    /// источника (CardModel.TierSource) ≥ Threshold; уровни задаются по возрастанию Threshold.
    ///
    /// Способности ВСЕХ уровней инитятся сразу (InitAbilities), но каждая срабатывает только на СВОЁМ уровне —
    /// на ability-сущности висит AbilityTierGateComponent{tierIndex}, а AbilityFire.Mark гейтит по текущему
    /// уровню. Рантайм-переподписки нет → синк даром (уровень зеркален, т.к. источник зеркален).
    /// Статы/стоимость применяются ДИФФ-модификаторами (Base не трогаем) — CardTierSystem.
    /// </summary>
    [System.Serializable]
    public class CardTier
    {
        [Tooltip("Значение источника (напр. золото Max) ≥ этого → уровень активен. Уровни по возрастанию Threshold.")]
        public int Threshold;
        public int Attack;
        public int Health;
        public int Speed;

        [Tooltip("Тип стоимости уровня (обычно совпадает с базовым PlayCost карты).")]
        public EnumService.ResourceType PlayCost;
        [Tooltip("Величина стоимости на уровне.")]
        public int PlayCostAmount;

        [Tooltip("Набор способностей уровня — активны только когда карта на этом уровне (гейт).")]
        [SerializeReference] public List<AbilityRuntime> Abilities = new();
    }
}
