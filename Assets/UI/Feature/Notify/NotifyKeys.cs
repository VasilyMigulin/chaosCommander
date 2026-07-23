using Game.Core.Shared;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// Реестр известных ключей уведомлений (бейджей «новое»). Используй типобезопасно:
    /// NotifyState.Set(NotifyKeys.BlackMarket, true) — вместо строки "blackmarket".
    /// AwesomeButton._notifyKey тоже заполняется этим значением (в инспекторе — строка Value).
    /// </summary>
    public static class NotifyKeys
    {
        public static readonly RegistryId Journal     = new RegistryId("journal");
        public static readonly RegistryId BlackMarket = new RegistryId("blackmarket");
        public static readonly RegistryId Auction     = new RegistryId("auction");
        public static readonly RegistryId Shop        = new RegistryId("shop");
        public static readonly RegistryId Boosters    = new RegistryId("boosters");
    }
}
