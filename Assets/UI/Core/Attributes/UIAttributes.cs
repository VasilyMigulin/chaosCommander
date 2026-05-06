using System;

namespace AwesomeUI.Core.Attributes
{
    /// <summary>
    /// Помечает поле для автоматической инъекции зависимостей
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class UIInjectAttribute : Attribute { }

    /// <summary>
    /// Помечает класс как UI элемент определённого типа (для генерации кода)
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class UIElementAttribute : Attribute
    {
        public UIElementType ElementType { get; }
        public string MenuPath { get; }

        public UIElementAttribute(UIElementType type, string menuPath = null)
        {
            ElementType = type;
            MenuPath = menuPath;
        }
    }

    public enum UIElementType
    {
        Canvas,
        Panel,
        Layout,
        Window,
        Slot
    }

    /// <summary>
    /// Автоматически подписывает метод на UI событие
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class UIEventHandlerAttribute : Attribute
    {
        public Type EventType { get; }

        public UIEventHandlerAttribute(Type eventType)
        {
            EventType = eventType;
        }
    }
}