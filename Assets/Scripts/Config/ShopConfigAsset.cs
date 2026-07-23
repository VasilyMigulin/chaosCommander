using System.Collections.Generic;
using Game.Core.Instance;
using UnityEngine;

namespace Game.Core.Configs
{
    // === class (ScriptableObject) ===
    /// <summary>
    /// Авторинг витрины магазина В UNITY: список офферов (карта/аватар ССЫЛКОЙ на InstanceData, бустер —
    /// явным itemId). Экспортёр (Tools → Backend → Export Shop Config) выгружает Title Data JSON "shopConfig"
    /// с ПРАВИЛЬНЫМИ нижними itemId (берёт их из инстанса) — руками JSON не редактируешь и с регистром не
    /// ошибаешься. Цены/категории живут ЗДЕСЬ и на сервере; клиент присылает лишь itemId (подделать цену нельзя).
    ///
    /// itemId ОБЯЗАН существовать в Catalog v1 (иначе GrantItemsToUser упадёт и покупка откатится).
    /// НЕ дублируй itemId в двух включённых офферах — сервер (findShopEntry) берёт первый, ломается «Куплено».
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Shop Config", fileName = "ShopConfig")]
    public class ShopConfigAsset : ScriptableObject
    {
        /// <summary>Auto = определить категорию по префиксу itemId (booster_/avatar_/иначе card).</summary>
        public enum Category { Auto, Booster, Avatar, Card }

        [System.Serializable]
        public class Entry
        {
            [Tooltip("Предмет витрины ссылкой (CardInstanceData/AvatarInstanceData). Для БУСТЕРА оставь пустым и впиши ItemIdOverride.")]
            public InstanceData Item;

            [Tooltip("Явный itemId, если ссылки нет (бустеры: booster_standard). Если задан — перекрывает Item.")]
            public string ItemIdOverride;

            [Tooltip("Подпись в витрине — текст ИЛИ ключ локализации (клиент прогонит через CardTextLocalization).")]
            public string DisplayName;

            [Tooltip("Auto = по префиксу itemId. Задай явно, если префикс не отражает категорию.")]
            public Category CategoryOverride = Category.Auto;

            [Tooltip("Код валюты: GD (золото) / GM (самоцветы).")]
            public string PriceCode = "GD";
            public int PriceAmount = 100;

            [Tooltip("Сколько выдать за одну покупку (наборы «×3»). По умолчанию 1. Для набора заведи ОТДЕЛЬНЫЙ itemId в каталоге.")]
            public int Quantity = 1;

            [Tooltip("Durable (аватары): повторно купить нельзя — в витрине «Куплено».")]
            public bool Unique;

            [Tooltip("Снять позицию из витрины, не удаляя её отсюда.")]
            public bool Enabled = true;

            /// <summary>Итоговый itemId (override приоритетнее ссылки), нижним регистром.</summary>
            public string ResolveItemId()
            {
                string id = !string.IsNullOrEmpty(ItemIdOverride) ? ItemIdOverride
                          : (Item != null ? Item.ItemId : null);
                return string.IsNullOrEmpty(id) ? null : id.ToLowerInvariant();
            }

            /// <summary>Категория: явная, иначе по префиксу itemId.</summary>
            public string ResolveCategory()
            {
                if (CategoryOverride != Category.Auto) return CategoryOverride.ToString().ToLowerInvariant();
                string id = ResolveItemId() ?? "";
                if (id.StartsWith("booster_")) return "booster";
                if (id.StartsWith("avatar_")) return "avatar";
                return "card";
            }
        }

        public List<Entry> Entries = new List<Entry>();
    }
}
