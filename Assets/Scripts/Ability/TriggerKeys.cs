namespace Game.Core.Ability
{
    /// <summary>
    /// Тип триггера для МНОЖИТЕЛЯ частоты (SetTriggerMultiplierEffect / CastMultiplierService). Значение enum
    /// → строковый ключ через TriggerKeys.Of. Чтобы множитель работал на триггере, сам триггер обязан слать
    /// этот ключ в AbilityFire.Mark (см. OnCastTrigger/OnDieTrigger/OnTurnStart/End). Расширяется: добавить
    /// значение в enum + ключ в TriggerKeys + передать его в Mark соответствующего триггера.
    /// </summary>
    public enum TriggerKind
    {
        OnTurnStart,   // начало хода
        OnTurnEnd,     // конец хода
        OnCast,        // при разыгрывании (батлкрай)
        OnDie,         // предсмертный хрип (deathrattle)
    }

    internal static class TriggerKeys
    {
        public const string OnTurnStart = "OnTurnStart";
        public const string OnTurnEnd   = "OnTurnEnd";
        public const string OnCast      = "OnCast";
        public const string OnDie       = "OnDie";

        public static string Of(TriggerKind k) => k switch
        {
            TriggerKind.OnTurnStart => OnTurnStart,
            TriggerKind.OnTurnEnd   => OnTurnEnd,
            TriggerKind.OnCast      => OnCast,
            TriggerKind.OnDie       => OnDie,
            _ => null,
        };
    }
}
