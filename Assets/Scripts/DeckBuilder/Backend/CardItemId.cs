namespace Game.Core.Backend
{
    /// <summary>
    /// Маппинг между идентификатором карты в игре (ExpansionId + CardId) и Item Id каталога
    /// PlayFab Economy v2. Контракт: friendly item id = "{expansionId}_{cardId}".
    /// Совпадает с PlayerLibrary.MakeKey и с catalog-seeding editor-tool — держать в синке.
    /// </summary>
    public static class CardItemId
    {
        /// <summary>Карта → item id каталога.</summary>
        public static string Of(string expansionId, int cardId) => $"{expansionId}_{cardId}";

        /// <summary>Распарсить item id карты обратно в (expansionId, cardId).
        /// Возвращает false для не-карточных предметов (бустеры/аватары/валюта).</summary>
        public static bool TryParse(string itemId, out string expansionId, out int cardId)
        {
            expansionId = null;
            cardId = -1;
            if (string.IsNullOrEmpty(itemId)) return false;
            if (IsBooster(itemId) || IsAvatar(itemId)) return false;

            int sep = itemId.LastIndexOf('_');
            if (sep <= 0 || sep >= itemId.Length - 1) return false;

            string idPart = itemId.Substring(sep + 1);
            if (!int.TryParse(idPart, out cardId)) return false;

            expansionId = itemId.Substring(0, sep);
            return true;
        }

        public static bool IsCard(string itemId) => TryParse(itemId, out _, out _);
        public static bool IsBooster(string itemId) => itemId != null && itemId.StartsWith(BackendConfig.BoosterIdPrefix);
        public static bool IsAvatar(string itemId)  => itemId != null && itemId.StartsWith(BackendConfig.AvatarIdPrefix);
    }
}
