using System.Collections.Generic;

namespace AwesomeUI.Feature
{
    public enum NotifyKind { Info, Success, Reward, Warning }

    public struct NotifyData
    {
        public string Text;
        public NotifyKind Kind;
    }

    /// <summary>
    /// Независимые всплывающие уведомления (тосты) поверх ЛЮБОГО UI. Любой код зовёт
    /// NotifyService.Success("...") и т.п. — презентер (NotifyPopupView) на верхнем оверлей-канвасе
    /// показывает тост. До появления презентера пуши копятся в очереди.
    /// </summary>
    public static class NotifyService
    {
        static NotifyPopupView _view;
        static readonly Queue<NotifyData> _pending = new Queue<NotifyData>();

        public static void Register(NotifyPopupView view)
        {
            _view = view;
            while (_pending.Count > 0) _view.Enqueue(_pending.Dequeue());
        }

        public static void Unregister(NotifyPopupView view)
        {
            if (_view == view) _view = null;
        }

        public static void Push(string text, NotifyKind kind = NotifyKind.Info)
        {
            var data = new NotifyData { Text = text, Kind = kind };
            if (_view != null) _view.Enqueue(data);
            else _pending.Enqueue(data);
        }

        public static void Info(string text)    => Push(text, NotifyKind.Info);
        public static void Success(string text) => Push(text, NotifyKind.Success);
        public static void Reward(string text)  => Push(text, NotifyKind.Reward);
        public static void Warning(string text) => Push(text, NotifyKind.Warning);
    }
}
