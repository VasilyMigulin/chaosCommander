namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Маркер «эту только что созданную карту нужно разыграть автоматически» (Фокус-покус: случайные спеллы
    /// из пула). Вешается CreateCardSystem по CreateCardEvent.AutoCast. AutoCastSystem (гейт IsLocalActive)
    /// у активного ставит RequestCardCastEvent{Free} и снимает маркер; у пассива просто снимает (каст придёт
    /// реплеем обычными ActionCastData/ActionAbilityData). Транзиентный — живёт до первого тика AutoCastSystem.
    /// </summary>
    public struct AutoCastComponent { }

    /// <summary>
    /// Маркер «таргетинг этой карты авто-выбирает цели (Random), НЕ передавая управление игроку» (как Йогг-
    /// Сарон). Ставится вместе с AutoCastComponent при авто-розыгрыше: RunAbilityTargetingSystem трактует
    /// Selected как Random → нет режима выбора цели/софт-лока. ПЕРСИСТЕНТНЫЙ (нужен на этапе таргетинга, который
    /// идёт ПОСЛЕ снятия AutoCastComponent); уходит вместе с картой в кладбище, безвреден.
    /// </summary>
    public struct ForceRandomTargetingComponent { }
}
