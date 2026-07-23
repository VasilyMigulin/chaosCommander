using AwesomeUI.Core.Card;
using Game.Core.Backend;
using Game.Core.Configs;
using UnityEngine;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// Единый резолвер иконок меты: «дай спрайт по коду валюты ИЛИ по каталожному itemId», без знания типа.
    /// Общий для магазина/наград/журнала. Валюта → InterfaceConfig; предмет → InstanceData.Miniature.
    /// Бустер — это тот же предмет с миниатюрой, отдельной ветки не нужно.
    /// </summary>
    public static class MetaIcon
    {
        /// <summary>Иконка валюты по коду ("GD"/"GM"). Источник — InterfaceConfig.</summary>
        public static Sprite Currency(string code)
            => InterfaceConfig.Current != null ? InterfaceConfig.Current.GetCurrencyIcon(code) : null;

        /// <summary>Миниатюра предмета по itemId (InstanceData.Miniature). Пока умеет аватары (статичный
        /// AvatarConfig); карты/бустеры подключатся, когда у них появятся статичные конфиги-реестры.</summary>
        public static Sprite Item(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return null;
            var avatar = AvatarConfig.Instance != null ? AvatarConfig.Instance.Get(itemId) : null;
            return avatar != null ? avatar.Miniature : null;
        }

        /// <summary>Валюта → иконка валюты; иначе предмет → миниатюра. Одна точка для «награда = иконка + число».</summary>
        public static Sprite Reward(string currencyCode, string itemId)
            => !string.IsNullOrEmpty(currencyCode) ? Currency(currencyCode) : Item(itemId);

        /// <summary>
        /// Иконка + количество ГЛАВНОЙ награды бандла (для компактного слота: одна иконка + число). Приоритет:
        /// валюта → карта → бустер → аватар. Награда обычно одиночная; если в бандле несколько — берём первую.
        /// </summary>
        public static Sprite RewardBundleIcon(RewardBundle b, out int amount)
        {
            amount = 0;
            if (b == null) return null;
            if (b.Currencies != null && b.Currencies.Count > 0) { amount = b.Currencies[0].Amount; return Currency(b.Currencies[0].Code); }
            if (b.Cards != null && b.Cards.Count > 0)            { amount = b.Cards[0].Amount;      return Item(b.Cards[0].ItemId); }
            if (b.Boosters != null && b.Boosters.Count > 0)      { amount = 1;                      return Item(b.Boosters[0]); }
            if (b.Avatars != null && b.Avatars.Count > 0)        { amount = 1;                      return Item(b.Avatars[0]); }
            return null;
        }
    }
}
