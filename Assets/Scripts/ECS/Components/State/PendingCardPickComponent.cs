namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Вешается на entity ИГРОКА когда он находится в режиме выбора карты (раскопка).
    /// CardPickSelectionSystem использует этот компонент чтобы знать:
    ///  - какая карта ждёт разыгрывания
    ///  - какие entity карт предложены игроку
    /// </summary>
    public struct PendingCardPickComponent
    {
        /// <summary>Entity карты которую игрок хочет сыграть.</summary>
        public int CastingCardEntity;

        /// <summary>Entity-ссылки предложенных карт (из источника).</summary>
        public int[] OfferedCardEntities;

        /// <summary>Количество реально заполненных слотов в OfferedCardEntities.</summary>
        public int OfferedCount;

        /// <summary>Токен окна выбора (PickRequestId) — ключ корреляции CardPickChosenEvent.</summary>
        public int RequestId;

        /// <summary>Оффер опубликован (слот у CardPickBrokerSystem получен).</summary>
        public bool Presented;
    }
}
