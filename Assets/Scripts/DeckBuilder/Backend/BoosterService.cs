using System;
using System.Collections.Generic;
using PlayFab.ClientModels;

namespace Game.Core.Backend
{
    /// <summary>
    /// Открытие бустеров. Ролл карт — на сервере (Azure Function OpenBooster) по дроп-таблице
    /// из Title Data: проверяет владение → списывает бустер → роллит карты по весам редкости →
    /// выдаёт в инвентарь → возвращает список для reveal-анимации. RNG на сервере = нечитабельно.
    /// </summary>
    public static class BoosterService
    {
        [Serializable] public class OpenRequest { public string BoosterItemId; }

        /// <summary>Ответ открытия: результат + выданные карты (Reward.Cards) для показа.</summary>
        [Serializable] public class OpenResponse : RewardResponse { }

        /// <summary>Список бустеров в инвентаре игрока (сгруппировано item id → количество).</summary>
        public static void GetOwned(Action<Dictionary<string, int>> onSuccess, Action<string> onError = null)
        {
            EconomyService.GetInventory(inv =>
            {
                var map = new Dictionary<string, int>();
                foreach (ItemInstance b in inv.Boosters)
                {
                    if (b?.ItemId == null) continue;
                    map.TryGetValue(b.ItemId, out var cur);
                    map[b.ItemId] = cur + 1;   // v1: каждый инстанс = 1 бустер
                }
                onSuccess?.Invoke(map);
            }, onError);
        }

        /// <summary>Открыть один бустер. Ответ.Reward.Cards = выпавшие карты.</summary>
        public static void Open(string boosterItemId, Action<OpenResponse> onSuccess, Action<string> onError = null)
            => FunctionService.Call<OpenRequest, OpenResponse>(
                BackendConfig.Fn.OpenBooster, new OpenRequest { BoosterItemId = boosterItemId },
                onSuccess, onError);
    }
}
