using System;

namespace Game.Core.Backend
{
    /// <summary>
    /// «Входящие»: награды/новости, выданные удалённо (пока игрок не в игре). При входе клиент
    /// запрашивает список и показывает через WindowNewPopup. Сервер, отдав записи, помечает их
    /// как показанные (чтобы не всплывали повторно) — это в CloudScript-функции GetInbox.
    /// </summary>
    public static class InboxService
    {
        public static void GetInbox(Action<InboxResponse> onSuccess, Action<string> onError = null)
            => FunctionService.Call(BackendConfig.Fn.GetInbox, onSuccess, onError);
    }
}
