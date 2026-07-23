namespace Game.Core.Service
{
    // === helper ===
    /// <summary>
    /// Сколько Обрывков даёт распыление («Порвать») карты по редкости. Клиент показывает это число на кнопке;
    /// сервер начисляет столько же (значения продублированы в CloudScript DustCard — держать в синхроне).
    /// Common 2 · Rare 4 · Epic 8 · Legendary 16 · Exotic 32.
    /// </summary>
    public static class DustValues
    {
        public static int For(EnumService.Rarity rarity) => rarity switch
        {
            EnumService.Rarity.Common    => 2,
            EnumService.Rarity.Rare      => 4,
            EnumService.Rarity.Epic      => 8,
            EnumService.Rarity.Legendary => 16,
            EnumService.Rarity.Exotic    => 32,
            _                            => 0,
        };
    }
}
