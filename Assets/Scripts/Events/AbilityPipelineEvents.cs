namespace Game.Core.Events
{
    // === struct (bus events нового пайплайна способностей) ===
    // Живут в инфраструктуре Game.Core.Events (как и остальные bus-события), публикуются
    // через GameEventBus. Это НОВЫЕ события «с нуля» — старые легаси-события не используем.

    /// <summary>Карта разыграна (финиш каста). Источник — RunCastRouterSystem.</summary>
    public struct CardCastEvent : IGameEvent { public int CardEntity; }

    /// <summary>Существо ВЫШЛО на поле боя (любым способом: розыгрыш / призыв / генерация на борд). В отличие
    /// от CardCastEvent («при разыгрывании» — только настоящий каст), это сигнал «появился на столе»
    /// для реакций ДРУГИХ: реактивные ауры (OnCreatureInvokedTrigger) и будущие «на выходе»-способности.
    /// Призыв (НЕ каст) публикует только это, без CardCastEvent → нет каскада «при разыгрывании».
    /// Generated=true — токен, созданный СРАЗУ на борд (CreateCardSystem, FillRow/SpawnCardOnBoard):
    /// CollectActionSystem такие НЕ синкает ActionCastData (создание уже зеркально через детерм. ре-ран).</summary>
    public struct CreatureInvokedEvent : IGameEvent { public int CardEntity; public bool Generated; public int OwnerId; }

    /// <summary>ЧАРА появилась на столе через ГЕНЕРАЦИЮ (SpawnCharmTokenEffect и т.п. — CreateCardEvent{InBoard}),
    /// а не через обычный розыгрыш из руки (тот получает CardCastEvent штатно, роутер публикует его сам).
    /// Аналог CreatureInvokedEvent{Generated=true}, но для чар: InBoard-создание в CreateCardSystem НЕ шлёт
    /// CardCastEvent вообще (см. его комментарий) — сгенерированная чара иначе не имела бы НИКАКОГО «я появилась»
    /// сигнала для своего OnCast-эквивалента. Нужен картам вида «Королёвский садовник»: чара, заспавненная
    /// эффектом, должна сразу (не на будущий ход) применить своё разовое действие — трекинг/бафф текущих
    /// целей — под ТЕМ ЖЕ источником (собой), что и её реактивная часть (иначе два разных TrackedBuffsComponent
    /// на разных картах-источниках дают двойной бафф существу, которое умерло и вернулось на поле).</summary>
    public struct CharmInvokedEvent : IGameEvent { public int CardEntity; public int OwnerId; }

    /// <summary>Существо-карта погибла.</summary>
    public struct CreatureDiedEvent : IGameEvent { public int CardEntity; public int KillerEntity; }

    /// <summary>Карта СБРОШЕНА из руки в кладбище (DiscardEffect). Ловит OnDiscardTrigger («При сбросе»),
    /// как OnDieTrigger ловит CreatureDiedEvent. Публикуется на активе (сброс симулирует активный клиент),
    /// пассив реплеит результат снапшотом резолва.</summary>
    public struct CardDiscardedEvent : IGameEvent { public int CardEntity; }

    /// <summary>Игрок получил урон (для аккумулирующих условий эффектов).</summary>
    public struct PlayerDamagedEvent : IGameEvent { public int PlayerEntity; public int Amount; }

    /// <summary>Карта СОЗДАНА/ЗАМЕШАНА эффектом (Generate). GeneratorPlayerId — PlayerId владельца ИСТОЧНИКА
    /// (кто сгенерировал, не обязательно владелец карты). Для матч-счётчика «создано» (Гнидальф).
    /// Публикуют helpers GenerateCardEffect.* на обоих клиентах (генерация детерминирована) → счётчик зеркален.</summary>
    public struct CardGeneratedEvent : IGameEvent { public int ModelId; public int GeneratorPlayerId; }
}
