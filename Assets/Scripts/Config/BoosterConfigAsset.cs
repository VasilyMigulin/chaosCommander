using System;
using System.Collections.Generic;
using Game.Core.Service;
using UnityEngine;

namespace Game.Core.Configs
{
    // === class (ScriptableObject) ===
    /// <summary>
    /// Авторинг бустеров В UNITY: на каждый бустер — визуал (иконка + ПРЕФАБ-вьюшка пачки для анимации
    /// открытия) + набор (сет = ExpansionConfig) + дроп-слоты (веса редкостей). Дроп-часть (expansion+slots)
    /// экспортёр выгружает в Title Data "boosterConfig" — по ней РОЛЛИТ СЕРВЕР (OpenBooster), клиенту дропы
    /// не доверяем. Визуал (Icon/PackViewPrefab/DisplayName) — только на клиенте, на сервер не идёт.
    ///
    /// PackViewPrefab — почему префаб: чтобы к экрану открытия сделать свою анимацию (пачка «рвётся», из неё
    /// веером вылетают карты). BoosterRevealView инстанцирует его как пачку.
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Booster Config", fileName = "BoosterConfig")]
    public class BoosterConfigAsset : ScriptableObject
    {
        [Serializable]
        public class RarityWeight
        {
            public EnumService.Rarity Rarity;
            [Range(0f, 1f)] public float Weight = 0.5f;
        }

        [Serializable]
        public class Slot
        {
            [Tooltip("Веса редкостей в этом слоте (нормализуются на сервере). Один слот = одна выпавшая карта.")]
            public RarityWeight[] Weights;
        }

        [Serializable]
        public class Booster
        {
            [Tooltip("Catalog ItemId бустера (нижний регистр), напр. booster_standard.")]
            public string ItemId = "booster_";
            [Tooltip("Подпись/ключ локализации (слот в списке + заголовок ревила).")]
            public string DisplayName;
            [Tooltip("Иконка бустера в списке. Клиентское.")]
            public Sprite Icon;
            [Tooltip("ПРЕФАБ-вьюшка пачки для экрана открытия (рвётся, из неё вылетают карты). Клиентское, на сервер НЕ идёт.")]
            public GameObject PackViewPrefab;
            [Tooltip("Набор (сет), из которого падают карты — ExpansionConfig. Экспортёр берёт его ExpansionId.")]
            public ExpansionConfig Expansion;
            [Tooltip("Слоты дропа: сколько карт (= число слотов) и с какими весами редкостей.")]
            public Slot[] Slots;
        }

        public List<Booster> Boosters = new List<Booster>();

        /// <summary>Найти бустер по ItemId (для клиентского визуала: иконка/имя/префаб-вьюшка).</summary>
        public Booster Get(string itemId)
        {
            if (string.IsNullOrEmpty(itemId) || Boosters == null) return null;
            string id = itemId.ToLowerInvariant();
            foreach (var b in Boosters)
                if (b != null && !string.IsNullOrEmpty(b.ItemId) && b.ItemId.ToLowerInvariant() == id) return b;
            return null;
        }
    }
}
