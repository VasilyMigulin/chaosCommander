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

    /// <summary>
    /// Ось «ТИП КАРТЫ» для множителя частоты: ключ множителя = комбинация «Тип/Триггер»
    /// (CastMultiplierService.ComposeKey): Spell/OnCast — «заклинания срабатывают дважды»,
    /// Creature/OnTurnEnd — «эффекты конца хода существ», Any — любой тип (Временная петля).
    /// ⚠️ Значения закреплены: Any=0 → старые ассеты (Временная петля) без поля читаются как Any.
    /// Имена типов совпадают с EnumService.CardType.ToString() (см. ScopeKey).
    /// </summary>
    public enum MultiplierCardScope { Any = 0, Creature = 1, Spell = 2, Charm = 3 }

    internal static class TriggerKeys
    {
        public const string OnTurnStart = "OnTurnStart";
        public const string OnTurnEnd   = "OnTurnEnd";
        public const string OnCast      = "OnCast";
        public const string OnDie       = "OnDie";

        /// <summary>Тип-часть составного ключа множителя ("*" / "Creature" / "Spell" / "Charm").</summary>
        public static string ScopeKey(MultiplierCardScope s) => s switch
        {
            MultiplierCardScope.Creature => nameof(Game.Core.Service.EnumService.CardType.Creature),
            MultiplierCardScope.Spell    => nameof(Game.Core.Service.EnumService.CardType.Spell),
            MultiplierCardScope.Charm    => nameof(Game.Core.Service.EnumService.CardType.Charm),
            _ => Game.Core.Ecs.Components.CastMultiplierService.AnyType,
        };

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
