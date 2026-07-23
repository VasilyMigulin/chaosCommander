using System;
using System.Collections.Generic;
using Game.Core.DeckBuilder;   // PlayerLibrary (та же сборка) — ресинк после расчёта лотов

namespace Game.Core.Backend
{
    /// <summary>
    /// Аукцион СО СТАВКАМИ (не «купи-сейчас»). Игроки выставляют СВОИ карты на 1 час, единая валюта
    /// на лот (золото ИЛИ гемы — выбор продавца). Другие делают ставки; побеждает высшая по дедлайну.
    /// Модель ставок специально убирает гонку «кто первый купил»: победитель определяется ОДИН раз
    /// на закрытии единым авторитетом (Scheduled Task → ResolveAuctions), а не в момент клика.
    ///
    /// Комиссия — buyer's premium: ставка = сколько ПОЛУЧИТ продавец; с победителя спишется ставка×(1+fee),
    /// разница сгорает (сток валюты). Ставка эскроуится сразу; проигравшим возврат при закрытии.
    ///
    /// Хранилище — Shared Group Data на сервере (лимит лотов = фишка «рынок вот-вот лопнет»). Клиент
    /// только листает/ставит/выставляет; все переводы — на сервере.
    /// </summary>
    public static class AuctionService
    {
        /// <summary>Один лот аукциона. CurrentBid/MyBid — в «молотковых» единицах (что получит продавец).</summary>
        [Serializable]
        public class Lot
        {
            public string LotId;
            public string ItemId;            // "{expansionId}_{cardId}"
            public string SellerId;
            public string SellerName;
            public string Currency;          // "GD"/"GM"
            public int    MinBid;            // стартовая ставка
            public int    CurrentBid;        // высшая ставка (0 = ставок нет)
            public string CurrentBidderId;
            public string CurrentBidderName;
            public int    BidCount;
            public int    MyBid;             // моя текущая ставка (0 = не ставил) — сервер считает для вызвавшего
            public string EndsAtUtc;         // ISO; клиент отсчитывает таймер
            public bool   Ended;             // дедлайн прошёл, ждём расчёта

            public bool TryResolve(out string expansionId, out int cardId)
                => CardItemId.TryParse(ItemId, out expansionId, out cardId);

            /// <summary>Сколько СПИШЕТСЯ с покупателя при ставке amount (с учётом комиссии).</summary>
            public static int WithFee(int amount, int feePercent)
                => amount + (amount * feePercent + 99) / 100;   // ceil(amount*fee/100)
        }

        /// <summary>Ответ GetAuctionListings: все открытые лоты + параметры рынка.</summary>
        [Serializable]
        public class AuctionState
        {
            public List<Lot> Lots = new List<Lot>();
            public int    MaxLots;           // лимит лотов на рынке (фишка «лопнет»)
            public int    LotCount;          // сколько сейчас занято
            public int    FeePercent;        // комиссия покупателя, %
            public int    LotDurationMin;    // длительность лота, мин (для подсказок)
            public string ServerTimeUtc;     // время сервера — для точного отсчёта таймеров

            public bool IsFull => MaxLots > 0 && LotCount >= MaxLots;
        }

        // ── Запросы ──────────────────────────────────────────────────────────────
        [Serializable] public class ListCardRequest { public string ItemId; public string Currency = BackendConfig.GoldCode; public int MinBid; }
        [Serializable] public class LotIdRequest    { public string LotId; }
        [Serializable] public class PlaceBidRequest { public string LotId; public int Amount; }

        // ── Ответы ───────────────────────────────────────────────────────────────
        [Serializable] public class ListResult    : BackendResult { public string LotId; }
        [Serializable] public class BidResult     : BackendResult { public int CurrentBid; public string EndsAtUtc; }
        [Serializable] public class ResolveResult : BackendResult { public int Resolved; }   // сколько лотов рассчитано

        // ── Чтение ───────────────────────────────────────────────────────────────

        /// <summary>Все открытые лоты + параметры рынка. «Мои» лоты/ставки клиент фильтрует по SellerId/MyBid.</summary>
        public static void GetListings(Action<AuctionState> onSuccess, Action<string> onError = null)
            => FunctionService.Call(BackendConfig.Fn.GetListings, onSuccess, onError);

        // ── Мутации ──────────────────────────────────────────────────────────────

        /// <summary>Выставить свою карту на 1 час. Карта сразу уходит из инвентаря в escrow.</summary>
        public static void ListCard(string itemId, string currency, int minBid,
            Action<ListResult> onSuccess, Action<string> onError = null)
            => FunctionService.Call<ListCardRequest, ListResult>(
                BackendConfig.Fn.ListCard,
                new ListCardRequest { ItemId = itemId, Currency = currency, MinBid = minBid },
                resp => { PlayerWallet.ApplyIfPresent(resp?.Wallet); onSuccess?.Invoke(resp); }, onError);

        /// <summary>Снять свой лот (только пока НЕТ ставок) — карта возвращается в инвентарь.</summary>
        public static void CancelListing(string lotId,
            Action<BackendResult> onSuccess, Action<string> onError = null)
            => FunctionService.Call<LotIdRequest, BackendResult>(
                BackendConfig.Fn.CancelListing, new LotIdRequest { LotId = lotId },
                resp => { PlayerWallet.ApplyIfPresent(resp?.Wallet); onSuccess?.Invoke(resp); }, onError);

        /// <summary>Сделать ставку amount (молотковую). Спишется amount×(1+комиссия); при перебивании — возврат на закрытии.</summary>
        public static void PlaceBid(string lotId, int amount,
            Action<BidResult> onSuccess, Action<string> onError = null)
            => FunctionService.Call<PlaceBidRequest, BidResult>(
                BackendConfig.Fn.PlaceBid, new PlaceBidRequest { LotId = lotId, Amount = amount },
                resp => { PlayerWallet.ApplyIfPresent(resp?.Wallet); onSuccess?.Invoke(resp); }, onError);

        /// <summary>
        /// Дев/крон: рассчитать истёкшие лоты (выдать карты победителям, заплатить продавцам, вернуть проигравшим).
        /// Если что-то рассчитано (Resolved>0) — пересинхронизирует локальную библиотеку+кошелёк с сервером, т.к.
        /// расчёт меняет инвентарь в обход клиента (иначе выданные/возвращённые карты видны только после рестарта).
        /// </summary>
        public static void ResolveNow(Action<ResolveResult> onSuccess = null, Action<string> onError = null)
            => FunctionService.Call<ResolveResult>(BackendConfig.Fn.ResolveAuctions,
                resp =>
                {
                    if (resp != null && resp.Resolved > 0 && BackendSession.Config != null)
                        PlayerLibrary.LoadFromInventory(BackendSession.Config,
                            () => onSuccess?.Invoke(resp), _ => onSuccess?.Invoke(resp));
                    else onSuccess?.Invoke(resp);
                }, onError);
    }
}
