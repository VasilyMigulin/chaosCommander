using System;
using Game.Core.DeckBuilder;   // PlayerLibrary (та же сборка)

namespace Game.Core.Backend
{
    /// <summary>
    /// DEV-гранты для быстрого теста сервиса (валюта/бустеры/карты). Идут через серверные функции,
    /// работающие ТОЛЬКО при включённом флаге Title Data `devMode` (иначе отказ) — чтобы не попало в прод.
    /// Обновляют кошелёк из ответа. Дёргается из DevCheatMenu.
    /// </summary>
    public static class DevService
    {
        [Serializable] class CurrencyReq  { public string Code; public int Amount; }
        [Serializable] class ItemReq      { public string ItemId; public int Count; }
        [Serializable] class ExpansionReq { public string ExpansionId; }

        public static void GrantCurrency(string code, int amount,
            Action<RewardResponse> onDone = null, Action<string> onError = null)
            => FunctionService.Call<CurrencyReq, RewardResponse>(
                BackendConfig.Fn.DevGrantCurrency, new CurrencyReq { Code = code, Amount = amount },
                r => { PlayerWallet.ApplyIfPresent(r?.Wallet); onDone?.Invoke(r); }, onError);

        public static void GrantBooster(string itemId, int count,
            Action<RewardResponse> onDone = null, Action<string> onError = null)
            => FunctionService.Call<ItemReq, RewardResponse>(
                BackendConfig.Fn.DevGrantBooster, new ItemReq { ItemId = itemId, Count = count },
                r => { PlayerWallet.ApplyIfPresent(r?.Wallet); onDone?.Invoke(r); }, onError);

        /// <summary>Сбросить журнал (прогресс/клеймы/серия входа) — прогнать цикл заново.</summary>
        public static void ResetJournal(Action onDone = null, Action<string> onError = null)
            => FunctionService.Call<object>(BackendConfig.Fn.DevResetJournal, null, () => onDone?.Invoke(), onError);

        /// <summary>Завершить все незаклеймленные задачи (прогресс → target) — проверить клейм разом.</summary>
        public static void CompleteTasks(Action onDone = null, Action<string> onError = null)
            => FunctionService.Call<object>(BackendConfig.Fn.DevCompleteTasks, null, () => onDone?.Invoke(), onError);

        /// <summary>Сбросить состояние чёрного рынка (снять «куплено» этой ротации) — купить заново.</summary>
        public static void ResetBlackMarket(Action onDone = null, Action<string> onError = null)
            => FunctionService.Call<object>(BackendConfig.Fn.DevResetBlackMarket, null, () => onDone?.Invoke(), onError);

        public static void GrantCard(string itemId, int count,
            Action<RewardResponse> onDone = null, Action<string> onError = null)
            => FunctionService.Call<ItemReq, RewardResponse>(
                BackendConfig.Fn.DevGrantCard, new ItemReq { ItemId = itemId, Count = count },
                r =>
                {
                    PlayerWallet.ApplyIfPresent(r?.Wallet);
                    PlayerLibrary.AddGranted(r?.Reward?.Cards, BackendSession.Config);   // карты → в библиотеку (коллекция обновится)
                    onDone?.Invoke(r);
                }, onError);

        /// <summary>Выдать ВСЮ коллекцию экспеншена (по 1 копии каждой карты) — из серверного cardPool.</summary>
        public static void GrantExpansion(string expansionId,
            Action<RewardResponse> onDone = null, Action<string> onError = null)
            => FunctionService.Call<ExpansionReq, RewardResponse>(
                BackendConfig.Fn.DevGrantExpansion, new ExpansionReq { ExpansionId = expansionId },
                r =>
                {
                    // Сервер оборачивает сбой выдачи в Success=false + Reason (напр. "grant_error: …"),
                    // а не в общий CloudScriptAPIRequestError — прокидываем текст в onError, чтобы был виден.
                    if (r == null || !r.Success) { onError?.Invoke(r?.Reason ?? "grant_failed"); return; }
                    PlayerWallet.ApplyIfPresent(r.Wallet);
                    PlayerLibrary.AddGranted(r.Reward?.Cards, BackendSession.Config);   // вся коллекция → в библиотеку
                    onDone?.Invoke(r);
                }, onError);
    }
}
