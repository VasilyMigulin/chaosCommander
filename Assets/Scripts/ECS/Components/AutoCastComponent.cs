namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Маркер «эту только что созданную/разыгранную карту нужно провести через каст-роутер» (Фокус-покус:
    /// случайные спеллы из пула; форс-плей с добора; PlayCardUtil — Барабук/Грядущий шторм и т.п.). Вешается
    /// CreateCardSystem по CreateCardEvent.AutoCast, OnDrawForcePlayTrigger или PlayCardUtil.Play. AutoCastSystem
    /// (гейт IsLocalActive) у активного ставит RequestCardCastEvent{Free} и снимает маркер; у пассива просто
    /// снимает (каст придёт реплеем обычными ActionCastData/ActionAbilityData). Транзиентный — живёт до первого
    /// тика AutoCastSystem. ПЕРСИСТЕНТНЫЙ (не в DelHere) — переживает границу кадра, в которой был поставлен
    /// (это и есть весь смысл: избежать гонки с DelHere<RequestCardCastEvent>, см. AutoCastSystem).
    /// </summary>
    public struct AutoCastComponent { public bool Free; }

    /// <summary>
    /// Маркер «таргетинг этой карты авто-выбирает цели (Random), НЕ передавая управление игроку» (как Йогг-
    /// Сарон). Ставится вместе с AutoCastComponent при авто-розыгрыше: RunAbilityTargetingSystem трактует
    /// Selected как Random → нет режима выбора цели/софт-лока. ПЕРСИСТЕНТНЫЙ (нужен на этапе таргетинга, который
    /// идёт ПОСЛЕ снятия AutoCastComponent); уходит вместе с картой в кладбище, безвреден.
    /// </summary>
    public struct ForceRandomTargetingComponent { }
}
