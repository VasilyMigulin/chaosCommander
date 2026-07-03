using System;
using System.Collections.Generic;
using PlayFab;
using PlayFab.ClientModels;
using UnityEngine;

namespace Game.Core.Backend
{
    /// <summary>
    /// Обёртка над PlayFab Economy v1. На клиенте — ЧТЕНИЕ (инвентарь + валюты одним GetUserInventory);
    /// все выдачи/списания идут через classic CloudScript (FunctionService). В v1 копии карты — это
    /// отдельные ItemInstance с одинаковым ItemId; количество = число инстансов по ItemId.
    /// </summary>
    public static class EconomyService
    {
        /// <summary>Разложенный инвентарь игрока.</summary>
        public class Inventory
        {
            public readonly List<ItemInstance> Cards    = new List<ItemInstance>();
            public readonly List<ItemInstance> Boosters = new List<ItemInstance>();
            public readonly List<ItemInstance> Avatars  = new List<ItemInstance>();
            public readonly List<CurrencyAmount> Currencies = new List<CurrencyAmount>();
        }

        /// <summary>Загрузить инвентарь + валюты игрока (один вызов) и разложить по категориям.</summary>
        public static void GetInventory(Action<Inventory> onSuccess, Action<string> onError = null)
        {
            PlayFabClientAPI.GetUserInventory(
                new GetUserInventoryRequest(),
                result =>
                {
                    var inv = new Inventory();

                    if (result.Inventory != null)
                        foreach (var item in result.Inventory)
                            Classify(item, inv);

                    if (result.VirtualCurrency != null)
                        foreach (var kv in result.VirtualCurrency)
                            inv.Currencies.Add(new CurrencyAmount { Code = kv.Key, Amount = kv.Value });

                    onSuccess?.Invoke(inv);
                },
                error =>
                {
                    Debug.LogWarning($"[EconomyService] GetUserInventory failed: {error.ErrorMessage}");
                    onError?.Invoke(error.ErrorMessage);
                });
        }

        static void Classify(ItemInstance item, Inventory acc)
        {
            if (item?.ItemId == null) return;
            if (CardItemId.IsBooster(item.ItemId))     acc.Boosters.Add(item);
            else if (CardItemId.IsAvatar(item.ItemId))  acc.Avatars.Add(item);
            else if (CardItemId.IsCard(item.ItemId))    acc.Cards.Add(item);
            // прочее игнорируем
        }

        /// <summary>Загрузить инвентарь и обновить кошелёк из валют.</summary>
        public static void RefreshWallet(Action onDone = null, Action<string> onError = null)
        {
            GetInventory(inv =>
            {
                PlayerWallet.SetAll(inv.Currencies);
                onDone?.Invoke();
            }, onError);
        }

        // ── Миграция старой библиотеки (UserData JSON → инвентарь v1) ───────────

        [Serializable] public class MigrateResult { public bool Migrated; public int CardsGranted; }

        /// <summary>
        /// Одноразовая серверная миграция player_library → инвентарь (идемпотентно, по флагу на сервере).
        /// Безопасно звать при каждом логине.
        /// </summary>
        public static void MigrateLibraryIfNeeded(Action<MigrateResult> onDone, Action<string> onError = null)
        {
            FunctionService.Call<MigrateResult>(BackendConfig.Fn.MigrateLibrary, onDone, onError);
        }
    }
}
