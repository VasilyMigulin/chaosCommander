using System;

namespace Game.Core.DeckBuilder
{
    /// <summary>
    /// Персистентные данные о владении картой.
    /// Сохраняется в PlayerPrefs в виде JSON.
    /// </summary>
    [Serializable]
    public struct OwnedCardData
    {
        public string ExpansionId;
        public int    CardId;
        public int    Count;

        public OwnedCardData(string expansionId, int cardId, int count)
        {
            ExpansionId = expansionId;
            CardId      = cardId;
            Count       = count;
        }

        /// <summary>Ссылка заполнена (карта выбрана).
        ///
        /// НЕ проверять это как <c>CardId != 0</c>: ноль — ВАЛИДНЫЙ Id (первая карта экспаншена, напр.
        /// «Шальной принц» stolen_princess/0). Такая проверка стояла в InitDeckSystem и BattleState и
        /// молча роняла матч: командира с Id 0 просто не создавали, игрок оставался без командира, а
        /// после мулигана UI вис (софт-лок, 2026-08-01). Признак пустоты — ОТСУТСТВИЕ ExpansionId:
        /// у незаполненной структуры он null/"", а у любой реальной карты каталога он есть.</summary>
        public bool IsSet => !string.IsNullOrEmpty(ExpansionId);
    }
}
