namespace Game.Core.Ecs.Components
{
    // === struct (State, транзиентный) ===
    /// <summary>
    /// Способность прошла правила и стоит в очереди на разыгрывание. Хранит выбранные цели.
    /// RunResolveAbilityQueueSystem разрешает такие сущности ПО ОДНОЙ за тик и снимает state.
    /// </summary>
    public struct AbilityQueuedState
    {
        public int[] Targets;

        // Синк недетерминированной генерации (случайный выбор из пула): на ПАССИВЕ ReplayAbility кладёт сюда
        // присланные активом идентичности; RunResolveAbilityQueueSystem грузит их в GeneratedCardChannel
        // перед применением эффектов (эффект берёт оттуда вместо ролла). На активе — null (эффект роллит+Record).
        public string[] GeneratedExps;
        public int[]    GeneratedIds;
    }
}
