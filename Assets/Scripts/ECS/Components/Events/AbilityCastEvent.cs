namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// «Способности карты разыгрываются» — ставится на КАЖДУЮ ability-сущность из
    /// AbilityContainerComponent карты, когда карта успешно скастована.
    /// Run*Cost + Run*CardCastSystem ставят это после оплаты и переноса зоны.
    /// RunCastAbilitiesCardSystem — точка входа для триггер-флоу.
    /// </summary>
    public struct AbilityCastEvent
    {
        public int CardEntity;
        public int OwnerPlayerEntity;
    }
}
