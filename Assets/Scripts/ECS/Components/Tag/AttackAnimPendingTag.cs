namespace Game.Core.Ecs.Components
{
    /// <summary>
    /// Маркер на entity существа: анимация атаки запущена, ждём коллбэка.
    /// Пока висит — ввод заблокирован и очередь способностей не разблокируется.
    /// </summary>
    public struct AttackAnimPendingTag { }
}
