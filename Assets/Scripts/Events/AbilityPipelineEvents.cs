namespace Game.Core.Events
{
    // === struct (bus events нового пайплайна способностей) ===
    // Живут в инфраструктуре Game.Core.Events (как и остальные bus-события), публикуются
    // через GameEventBus. Это НОВЫЕ события «с нуля» — старые легаси-события не используем.

    /// <summary>Карта разыграна (финиш каста). Источник — RunCastRouterSystem.</summary>
    public struct CardCastEvent : IGameEvent { public int CardEntity; }

    /// <summary>Существо ВЫШЛО на поле боя (любым способом: розыгрыш / призыв). В отличие от
    /// CardCastEvent («при разыгрывании» — только настоящий каст), это сигнал «появился на столе»
    /// для реакций ДРУГИХ: реактивные ауры (OnCreatureInvokedTrigger) и будущие «на выходе»-способности.
    /// Призыв (НЕ каст) публикует только это, без CardCastEvent → нет каскада «при разыгрывании».</summary>
    public struct CreatureInvokedEvent : IGameEvent { public int CardEntity; }

    /// <summary>Существо-карта погибла.</summary>
    public struct CreatureDiedEvent : IGameEvent { public int CardEntity; public int KillerEntity; }

    /// <summary>Игрок получил урон (для аккумулирующих условий эффектов).</summary>
    public struct PlayerDamagedEvent : IGameEvent { public int PlayerEntity; public int Amount; }

    /// <summary>Карта СОЗДАНА/ЗАМЕШАНА эффектом (Generate). GeneratorPlayerId — PlayerId владельца ИСТОЧНИКА
    /// (кто сгенерировал, не обязательно владелец карты). Для матч-счётчика «создано» (Гнидальф).
    /// Публикуют helpers GenerateCardEffect.* на обоих клиентах (генерация детерминирована) → счётчик зеркален.</summary>
    public struct CardGeneratedEvent : IGameEvent { public int ModelId; public int GeneratorPlayerId; }
}
