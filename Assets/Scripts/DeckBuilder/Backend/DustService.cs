using System;
using Game.Core.DeckBuilder;   // PlayerLibrary (та же сборка)

namespace Game.Core.Backend
{
    /// <summary>
    /// Распыление карты в Обрывки («Порвать»). Сервер авторитетно определяет редкость (тег каталога),
    /// списывает копии и начисляет SC. Клиент обновляет кошелёк и убирает распылённые копии из библиотеки —
    /// коллекция обновляется в рантайме (PlayerLibrary.Changed). Поддерживает распыление ПАЧКОЙ («все дубли»).
    /// </summary>
    public static class DustService
    {
        [Serializable] class DustRequest { public string ItemId; public int Count = 1; }

        [Serializable]
        public class DustResponse : BackendResult   // Success/Reason/Wallet
        {
            public int Amount;           // сколько Обрывков начислено (за все распылённые)
            public int Dusted;           // сколько копий фактически распылено
            public int RemainingCount;   // сколько копий карты осталось у игрока
        }

        public static void Dust(string itemId, Action<DustResponse> onSuccess, Action<string> onError = null)
            => Dust(itemId, 1, onSuccess, onError);

        /// <summary>Распылить count копий карты за раз (для «порвать все дубли этой карты»).</summary>
        public static void Dust(string itemId, int count, Action<DustResponse> onSuccess, Action<string> onError = null)
            => FunctionService.Call<DustRequest, DustResponse>(
                BackendConfig.Fn.DustCard, new DustRequest { ItemId = itemId, Count = count },
                resp =>
                {
                    if (resp != null && resp.Success)
                    {
                        PlayerWallet.ApplyIfPresent(resp.Wallet);
                        if (CardItemId.TryParse(itemId, out var exp, out var cardId))
                        {
                            int removed = resp.Dusted > 0 ? resp.Dusted : 1;   // сколько сервер реально списал
                            PlayerLibrary.RemoveCard(exp, cardId, removed);    // −N копий локально
                            DeckStorage.PruneAgainstLibrary();                 // колоды не должны держать порванные карты
                        }
                    }
                    onSuccess?.Invoke(resp);
                }, onError);
    }
}
