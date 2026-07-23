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

    /// <summary>Критерий УМНОГО авто-выбора цели для ИИ (PvE): вместо случайного из валидных кандидатов
    /// RunAbilityTargetingSystem сортирует по критерию. Фильтры карты уже задали, КОГО можно выбирать
    /// (враг/союзник/зона) — здесь только «какого именно».</summary>
    public enum AiTargetPreference
    {
        None = 0,          // как раньше — случайно
        HighestAttack = 1, // самая опасная цель (removal/дебафф) или лучший получатель баффа
        LowestHealth = 2,  // добивание уроном
        MostDamaged = 3,   // лечение: макс. потерянного HP
    }

    /// <summary>
    /// «ИИ уже выбрал, как целиться» — вешается RunAiTurnSystem на КАРТУ вместе с
    /// ForceRandomTargetingComponent при касте. RunAbilityTargetingSystem при авто-выборе (random-ветка)
    /// сортирует кандидатов по Mode вместо шаффла. Только PvE (ИИ — единственный, кто ставит);
    /// персистентный, как ForceRandomTargetingComponent, безвреден после розыгрыша.
    /// </summary>
    public struct AiTargetPreferenceComponent { public AiTargetPreference Mode; }
}
