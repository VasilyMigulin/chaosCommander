namespace Game.Core.Service
{
    /// <summary>
    /// Глобальные тех-флаги для отладки/тестов. Лежат в Service (самая нижняя сборка без зависимостей),
    /// чтобы переключать из дев-оверлея (Mono) и читать из любых систем/сервисов.
    /// </summary>
    public static class DebugFlags
    {
        /// <summary>Сборка колоды без правила цвета (DeckBuilderService.IsColorAllowed → true).</summary>
        public static bool IgnoreDeckColorRule;
    }
}
