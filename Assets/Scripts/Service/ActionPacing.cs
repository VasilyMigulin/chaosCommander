namespace Game.Core.Service
{
    // === helper ===
    /// <summary>
    /// Базовая пауза между обработкой СОСЕДНИХ элементов очереди: резолв способностей у актива
    /// (RunResolveAbilityQueueSystem), реплей действий у пассива (ReplayActionSystem), автокаст ИИ
    /// (AutoCastSystem) и шаг цепочки/RepeatAbility (RunChainSystem). Зачем:
    ///   • читаемость — каскад эффектов/розыгрышей перестаёт быть «мешаниной» за 1-2 кадра;
    ///   • сеть — снапшоты (ActionAbilityData/ActionCastData) не улетают пачкой в соседние кадры.
    ///
    /// Это ЧИСТО тайминг: не меняет ни порядок действий, ни игровое состояние (как гейты анимации/снаряда) —
    /// значит на синк/детерминизм не влияет. 0 = без паузы (как раньше).
    ///
    /// GapSeconds — обычное статическое поле (не ScriptableObject-конфиг): значение читают 4 класса-системы
    /// в Game.Core.Ecs.Systems, а задаёт его BattleState (сериализованное поле в инспекторе, см. Awake) —
    /// та же причина, по которой здесь лежит KeyDropdownAttribute (Service.asmdef уже видят и Ecs.Systems,
    /// и States, лишняя ссылка сборок не нужна).
    /// </summary>
    public static class ActionPacing
    {
        public static float GapSeconds = 1.1f;
    }
}
