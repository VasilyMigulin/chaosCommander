namespace Game.Core.Ecs.Components
{
    public enum SummonFromZoneSource
    {
        OwnDeck = 0,
        OwnHand = 1,
        OwnGrave = 2,
        OpponentDeck = 3,
        OpponentHand = 4,
        OpponentGrave = 5,
    }

    public enum SummonFromZonePickMode
    {
        First = 0,        // первый подходящий (детерминированно)
        Random = 1,       // случайный из подходящих (стабильный seed)
        MaxCost = 2,      // самый дорогой
        MinCost = 3,      // самый дешёвый
    }

    /// <summary>
    /// Ищет N карт в указанной зоне по предикату (cost, modelId, цвет, тип) и
    /// перемещает их на доску владельца способности (с чередованием L/R по тому же
    /// принципу, что SummonEffect). Существующие сущности — без CreateCardEvent.
    /// </summary>
    public struct SummonFromZoneEffectComponent
    {
        public SummonFromZoneSource Source;
        public SummonFromZonePickMode PickMode;
        public int Count;

        public int CostMax;           // -1 = без верхнего лимита
        public int CostMin;           // 0  = без нижнего лимита
        public int ExactModelId;      // -1 = без фильтра по ModelId
        public Service.EnumService.Element RequiredColors;   // 0 = без требования
        public Service.EnumService.Element ForbiddenColors;  // 0 = без запрета
        public bool CreatureOnly;
        public bool SpellOnly;
        public bool ExcludeSelf;      // не брать карту-источник саму себя
    }
}
