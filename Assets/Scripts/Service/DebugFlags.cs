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

        /// <summary>
        /// Открыт ли дев-оверлей. ОДИН тумблер на все дев-панели (LogCopyOverlay — логи/бой/онбординг,
        /// DevCheatMenu — экономика): по умолчанию всё скрыто, на экране только кнопка «DEV». Так дев-кнопки
        /// не залепляют боевой/меню интерфейс. Флаг здесь (нижняя сборка) — обе панели живут в разных сборках.
        /// </summary>
        public static bool DevOverlayOpen;

        /// <summary>Открыт ли лог-оверлей (LogCopyOverlay: логи + тех-действия боя/онбординга). Отдельный
        /// тумблер от DevOverlayOpen — чтобы экономика и логи не открывались одновременно и не перекрывались.</summary>
        public static bool LogOverlayOpen;

        /// <summary>
        /// Залогинен АККАУНТ РАЗРАБОТЧИКА (флаг isDev в UserReadOnlyData — пишет только сервер/Game Manager,
        /// клиент подделать не может). Ставит бэкенд после логина. Включает дев-панель ДАЖЕ в релиз-сборке
        /// (в редакторе/dev-билде панель и так доступна). Сервер отдельно проверяет isDev для дев-грантов.
        /// </summary>
        public static bool DevAccount;

        /// <summary>Дев-UI разрешён: редактор, development-билд ИЛИ dev-аккаунт. Единый гейт для обоих оверлеев.</summary>
        public static bool DevUiAllowed =>
            UnityEngine.Application.isEditor || UnityEngine.Debug.isDebugBuild || DevAccount;
    }
}
