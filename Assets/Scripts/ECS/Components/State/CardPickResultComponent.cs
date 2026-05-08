namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Добавляется на entity КАРТЫ после того как игрок выбрал карту из предложенных.
    /// Эффекты способности читают этот компонент чтобы знать выбранную карту.
    /// Удаляется после завершения резолва способностей карты.
    /// </summary>
    public struct CardPickResultComponent
    {
        /// <summary>Entity выбранной карты.</summary>
        public int ChosenCardEntity;

        /// <summary>ModelId выбранной карты (на случай если entity уже не существует).</summary>
        public int ChosenCardModelId;
    }
}
