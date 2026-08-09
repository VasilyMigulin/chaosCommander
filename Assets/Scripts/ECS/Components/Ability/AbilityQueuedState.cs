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

        /// <summary>Порядок разыгрывания. Резолв берёт МИНИМАЛЬНЫЙ ключ. Корень — (волна, порядок выхода
        /// карты на стол, AbilityIndex): «первым вышел — первым активировал» внутри одной волны каскада.
        /// Реакция (хрип и т.п.) наследует ключ своей причины и встаёт СРАЗУ ЗА НЕЙ — см. ActivationKey.
        /// ЛОКАЛЬНЫЙ: сортирует только актив, пассив реплеит его порядок из снапшотов.</summary>
        public ActivationKey Key;

        // Синк недетерминированной генерации (случайный выбор из пула): на ПАССИВЕ ReplayAbility кладёт сюда
        // присланные активом идентичности; RunResolveAbilityQueueSystem грузит их в GeneratedCardChannel
        // перед применением эффектов (эффект берёт оттуда вместо ролла). На активе — null (эффект роллит+Record).
        public string[] GeneratedExps;
        public int[]    GeneratedIds;
    }
}
