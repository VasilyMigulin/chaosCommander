namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Маркер: существо находится под временным контролем; на TurnEnd
    /// TempControlRevertSystem вернёт оригинальные OwnerId/Side/теги.
    /// </summary>
    public struct TempControlledComponent
    {
        public int OriginalOwnerId;
        public int OriginalSide;
        public bool OriginalWasOwn;  // имел OwnCardTag до захвата (на локальном клиенте)
        public int ExpiresOnPlayerId; // конец чьего хода завершает контроль
    }
}
