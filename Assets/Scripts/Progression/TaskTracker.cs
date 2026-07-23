namespace Game.Core.Progression
{
    /// <summary>
    /// База трекера задачи: подписывается на GameEventBus (ПЕРСИСТЕНТНО — переживает Clear() между матчами) и
    /// пишет прогресс в общий аккумулятор TaskProgress. Реальная отправка на сервер — батчем в конце матча
    /// (TaskTrackingService). Добавить новую задачу = новый наследник + запись в taskConfig.json; больше нигде.
    ///
    /// Фильтр «своё/чужое» трекеры берут из TaskTrackingService.LocalPlayerId (из PlayerAssignedEvent).
    /// </summary>
    public abstract class TaskTracker
    {
        /// <summary>Строка типа (см. TaskTypes) — по ней сервер инкрементит все задачи этого типа.</summary>
        public abstract string Type { get; }

        /// <summary>Подписаться на нужные события шины (персистентно).</summary>
        public abstract void Subscribe();

        /// <summary>Отписаться (обычно не зовётся — живём всё приложение).</summary>
        public abstract void Unsubscribe();

        /// <summary>Записать прогресс в аккумулятор (уйдёт на сервер батчем в конце матча).</summary>
        protected void Report(int amount) => TaskProgress.Add(Type, amount);
    }
}
