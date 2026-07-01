namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Карта-носитель модификатора мулигана (Били: «в начале матча начинаете с N карт и можете заменить
    /// любое количество»). Вешается на сущность карты в CardModel.Init из полей модели. InitMulliganSystem
    /// сканирует колоду игрока на наличие такого маркера ДО раздачи (мулиган идёт раньше способностей).
    /// Локально на каждом клиенте (мулиган свой), синк не требуется.
    /// </summary>
    public struct MulliganModifierComponent
    {
        /// <summary>Абсолютный размер стартовой руки (0 = не менять).</summary>
        public int StartingHand;
        /// <summary>Можно заменить любое число карт (maxReplacements = размер руки).</summary>
        public bool UnlimitedReplace;
    }
}
